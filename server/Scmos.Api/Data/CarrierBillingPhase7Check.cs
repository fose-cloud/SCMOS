using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

public static class CarrierBillingPhase7Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase7")) return null;
        var failed = 0;
        var noOriginal = BillingFinanceReadiness.Evaluate(true, false, false);
        failed += Check("approved without Original is not Finance ready", noOriginal.Ready, false);
        failed += Check("approved without Original remains awaiting it", noOriginal.InvoiceStatus, BillingInvoiceStatus.AwaitingOriginal);
        var ready = BillingFinanceReadiness.Evaluate(true, true, false);
        failed += Check("approved plus Original and no blocker is Finance ready", ready.Ready, true);
        failed += Check("a ready invoice enters READY_FOR_FINANCE", ready.InvoiceStatus, BillingInvoiceStatus.ReadyForFinance);
        var blocked = BillingFinanceReadiness.Evaluate(true, true, true);
        failed += Check("a blocking exception prevents Finance readiness", blocked.Ready, false);
        failed += Check("physical receipt remains visible while blocked", blocked.InvoiceStatus, BillingInvoiceStatus.OriginalReceived);
        failed += Check("Original cannot replace online approval", BillingFinanceReadiness.Evaluate(false, true, false).Ready, false);

        var emptyPolicy = new OriginalReceiptPolicy(new ConfigurationBuilder().Build());
        var supervisor = User("supervisor", Roles.Supervisor);
        failed += Check("Original receipt permission fails closed when unconfigured", emptyPolicy.CanReceive(supervisor), false);
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            [$"{OriginalReceiptPolicy.RolesKey}:0"] = Roles.Supervisor,
        }).Build();
        var policy = new OriginalReceiptPolicy(configured);
        failed += Check("configured role may receive Original", policy.CanReceive(supervisor), true);
        failed += Check("another internal role is not silently granted receipt", policy.CanReceive(User("operator", Roles.Operation)), false);
        failed += Check("carrier cannot receive Original even if configured", new OriginalReceiptPolicy(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                [$"{OriginalReceiptPolicy.RolesKey}:0"] = Roles.Subcontractor,
            }).Build()).CanReceive(User("carrier", Roles.Subcontractor)), false);

        var options = new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=scmos-phase7-model;Trusted_Connection=True").Options;
        using var db = new ScmosDbContext(options);
        var package = db.Model.FindEntityType(typeof(OriginalDocumentPackage));
        failed += Check("Original package is persisted", package is not null, true);
        failed += Check("one physical package is enforced per invoice", package?.GetIndexes().Any(index => index.IsUnique
            && index.Properties.Count == 1 && index.Properties[0].Name == nameof(OriginalDocumentPackage.InvoiceId)) == true, true);
        failed += Check("Original package records who received it", package?.FindProperty(nameof(OriginalDocumentPackage.ReceivedById)) is not null, true);
        failed += Check("Original package is linked to its invoice", package?.GetForeignKeys().Any(key =>
            key.Properties.Any(property => property.Name == nameof(OriginalDocumentPackage.InvoiceId))) == true, true);
        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 7 checks passed." : $"{failed} Carrier Billing Phase 7 check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static AppUser User(string id, string role) => new(id, $"{id}@local", id, role,
        id.ToUpperInvariant(), "test", true);
    private static int Check<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {why}");
        return ok ? 0 : 1;
    }
}
