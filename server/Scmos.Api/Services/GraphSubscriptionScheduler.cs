using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Keeps the mail subscriptions alive, hourly, for as long as the API is up.
///
/// <para>
/// <b>Nothing else notices when a subscription dies.</b> Graph expires one in
/// under three days and stops delivering without a word; the inbox simply goes
/// quiet, which is indistinguishable from a quiet week. So this loop is not a
/// convenience — it is the only thing standing between a working integration
/// and one that stopped on Tuesday and was noticed on Friday.
/// </para>
///
/// <para>
/// The same shape as <see cref="ReportScheduler"/>, and for the same reasons:
/// inside the API because Always On means it is already awake, hourly because a
/// daily timer fires relative to whenever the container last booted, and
/// written to be interrupted because App Service recycles whenever it likes.
/// "Is this already done" is a question about the row's expiry, never about a
/// variable held since the last tick.
/// </para>
///
/// <para>
/// Quiet by default. With no mailbox marked active, or no webhook configured,
/// it does nothing at all and says so once rather than hourly — an integration
/// that logs a failure every hour before anybody has switched it on is an
/// integration whose logs nobody reads by the time it matters.
/// </para>
/// </summary>
public class GraphSubscriptionScheduler(IServiceProvider services, ILogger<GraphSubscriptionScheduler> log)
    : BackgroundService
{
    /// <summary>
    /// Hourly, against a renewal window a full day wide — so roughly
    /// twenty-four chances to renew before delivery would stop.
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromHours(1);

    /// <summary>
    /// Nothing for the first few minutes. A container that restarts eleven
    /// times in a morning should not talk to Graph eleven times, and nothing
    /// here is urgent to the minute — the window is a day wide.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(3);

    /// <summary>
    /// What was said last time, so an unchanged complaint is not repeated
    /// hourly into the log for the weeks a configuration takes to arrive.
    /// </summary>
    private string _lastComplaint = "";

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try
        {
            await Task.Delay(Settle, stopping);
        }
        catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception problem)
            {
                // Never fatal. A failed sweep is retried next hour; a scheduler
                // that takes the API down with it is a worse outcome than a
                // subscription renewed late — and the window allows for it.
                log.LogError(problem, "Graph subscription sweep failed");
            }

            try
            {
                await Task.Delay(Tick, stopping);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SweepAsync(CancellationToken stopping)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var subscriptions = scope.ServiceProvider.GetRequiredService<GraphSubscriptionService>();
        var graph = scope.ServiceProvider.GetRequiredService<GraphAuth>();

        var mailboxes = await db.Mailboxes.Where(one => one.IsActive).ToListAsync(stopping);
        if (mailboxes.Count == 0)
        {
            Complain("No mailbox is switched on; nothing to subscribe to.");
            return;
        }

        if (subscriptions.WhyNotReady() is { } why)
        {
            Complain(why.Message);
            return;
        }

        _lastComplaint = "";
        var now = DateTimeOffset.UtcNow;

        foreach (var mailbox in mailboxes)
        {
            if (stopping.IsCancellationRequested) return;

            // A mailbox somebody switched on but the deployment never approved.
            // Worth saying out loud: it is a row that looks configured and will
            // never receive anything.
            if (!graph.Approves(mailbox.Address))
            {
                log.LogWarning("Mailbox {Mailbox} is switched on but is not in Graph__Mailboxes",
                    mailbox.Address);
                continue;
            }

            var live = await db.GraphSubscriptions
                .Where(one => one.MailboxId == mailbox.Id && one.Status == MailSubscription.Active)
                .OrderByDescending(one => one.ExpiresAt)
                .FirstOrDefaultAsync(stopping);

            if (live is null)
            {
                await SubscribeAsync(subscriptions, mailbox, stopping);
                continue;
            }

            switch (GraphSubscriptions.Due(live.Status, live.ExpiresAt, now))
            {
                case GraphSubscriptions.Do.Renew:
                    var renewed = await subscriptions.RenewAsync(live, stopping);
                    // A renewal that failed because Graph had already forgotten
                    // it leaves the row expired. Creating a new one now rather
                    // than next hour is an hour of mail that still arrives.
                    if (!renewed.Finding.Ok && live.Status != MailSubscription.Active)
                        await SubscribeAsync(subscriptions, mailbox, stopping);
                    break;

                case GraphSubscriptions.Do.Recreate:
                    live.Status = MailSubscription.Expired;
                    await db.SaveChangesAsync(stopping);
                    await SubscribeAsync(subscriptions, mailbox, stopping);
                    break;
            }
        }
    }

    private async Task SubscribeAsync(GraphSubscriptionService subscriptions, Mailbox mailbox,
        CancellationToken stopping)
    {
        // Best effort, and deliberately not a reason to stop: the id is what
        // keeps a renewal from following a reassigned address, but a
        // subscription against the address still delivers mail.
        await subscriptions.ResolveUserIdAsync(mailbox, stopping);

        var made = await subscriptions.CreateAsync(mailbox, stopping);
        if (!made.Finding.Ok)
            log.LogWarning("Could not subscribe to {Mailbox}: {Why}", mailbox.Address, made.Finding.Message);
    }

    /// <summary>Says it once, then stops until it changes or is fixed.</summary>
    private void Complain(string what)
    {
        if (string.Equals(_lastComplaint, what, StringComparison.Ordinal)) return;
        _lastComplaint = what;
        log.LogInformation("Graph subscriptions idle: {Why}", what);
    }
}
