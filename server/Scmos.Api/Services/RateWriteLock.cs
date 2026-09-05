using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

internal static class RateWriteLock
{
    // Transaction ownership releases the lock on either commit or rollback.
    public static Task<int> TakeAsync(ScmosDbContext db, string resource, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource},
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
            IF @result < 0 THROW 51000, 'Could not acquire rate write lock. Please retry.', 1;
            """, token);
}
