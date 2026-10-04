using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public partial class AiGateway
{
    // An internal, fixed adapter entry: public AuthorizeAsync never consumes ApprovalId.
    // Caller is OperationsChangeService, inside its serializable transaction. The same
    // transaction consumes the pending row, writes the exact values and verifies them.
    internal async Task<AiAuthorizationDecision> AuthorizeOperationsApprovalAsync(long id, string reviewedHash,
        AppUser approver, CancellationToken token)
    {
        var catalog = policies ?? AiPolicyCatalog.Current;
        var request = AiAuthorizationRequest.For(AgentIds.Operations, AiAction.BookingUpdateCriticalField,
            "update_shipment", approver, resourceType: "approval", resourceId: id.ToString())
            with { RiskLevel = AiRisk.High, ApprovalId = id.ToString() };
        Task<AiAuthorizationDecision> Refuse(string code)
            => AuthorizeCoreAsync(request with { InputValid = false }, token, rejection: code);
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            return await Refuse("approval_transaction_required");
        var row = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, token);
        OperationsChangePayload? payload;
        try { payload = row is null ? null : JsonSerializer.Deserialize<OperationsChangePayload>(row.Payload); }
        catch (JsonException) { payload = null; }
        if (row is null || payload is null || row.State != "pending" || reviewedHash != row.PayloadHash
            || !OperationsChangePolicy.BoundApproval(row, payload, catalog.Version)) return await Refuse("invalid_approval");
        if (payload.ExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow()) return await Refuse("approval_expired");
        if (!OperationsChangePolicy.IndependentApprover(approver, payload) || approver.Signature == row.RequestedBy)
            return await Refuse("self_approval_forbidden");
        if (approver.Strength != SignInStrength.MultiFactor) return await Refuse("approval_mfa_required");
        var reviewer = await db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == approver.OperatorId, token);
        var owner = await db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == payload.RequesterOperatorId, token);
        if (!OperationsChangePolicy.Assignable(reviewer)
            || !Roles.Can(reviewer!.Role, Capability.ApproveAi | Capability.EditAnyJob | Capability.AssignJobs)
            || !OperationsChangePolicy.Assignable(owner)) return await Refuse("approval_identity_revoked");
        var job = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Key == payload.Key, token);
        var requester = new AppUser(payload.RequesterId, owner!.Email, owner.Name, owner.Role, owner.Id,
            "bound-approval", true);
        if (job is null || !OperationsChangePolicy.Owns(requester, job)) return await Refuse("requester_forbidden");
        var before = OperationsChangePolicy.Values(job);
        if (OperationsChangePolicy.Fingerprint(job) != payload.Fingerprint
            || before.Count != payload.Before.Count || before.Any(p => !payload.Before.TryGetValue(p.Key, out var value) || value != p.Value))
            return await Refuse("approval_stale");
        request = AiAuthorizationRequest.For(payload.AgentId, AiAction.BookingUpdateCriticalField, row.Tool,
            requester, payload.CorrelationId, "shipment", payload.Key) with
        {
            AgentVersion = payload.AgentVersion, RiskLevel = AiRisk.High, ApprovalId = id.ToString()
        };
        return await AuthorizeCoreAsync(request, token, reviewedOperations: true);
    }
}
