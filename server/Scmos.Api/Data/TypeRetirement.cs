using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Retires the vehicle types the rule has retired, with <c>--retire-types</c>.
///
/// <para>
/// Retiring is an Administrator's click on the Capacity screen — one row at a
/// time, signed in. This does the same thing from the release workflow, where
/// the connection string already lives, for the codes
/// <see cref="JobVehicleType.Retired"/> names: the two dangerous-goods
/// containers that stopped being a kind of container when the cleanup moved
/// DG into the product column. Asked for by the department on 27 August and
/// again on 14 September 2026.
/// </para>
///
/// <para>
/// <b>Never a delete.</b> The row's <c>Active</c> flag goes false, exactly as
/// the screen's own button does; the code stays legible on every job that
/// carries it, and <c>AddAsync</c> brings it back. Idempotent — a second run
/// says "already retired" and changes nothing.
/// </para>
/// </summary>
public static class TypeRetirement
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        _ = args;
        using var scope = app.Services.CreateScope();
        var types = scope.ServiceProvider.GetRequiredService<VehicleTypeService>();

        var lines = await types.RetireRuledAsync("release --retire-types", default);
        foreach (var line in lines) app.Logger.LogInformation("{Line}", line);
        app.Logger.LogInformation("{Count} codes in the retired set; the list now offers what the rule offers.", lines.Count);
        return 0;
    }
}
