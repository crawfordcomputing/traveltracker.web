using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Data;

// Removes an evaluation batch written by EvalSeeder, leaving zero residue and
// never touching real data. Counterpart to EvalSeeder; both agree on naming via
// EvalNaming.
//
// Why the specific order: every AppUser foreign key in the model is Restrict (a
// user can't be deleted while anything still points at it), while every Trip->child
// relationship is Cascade (deleting a trip removes its Destinations / Expenses /
// splits / Mileage / waypoints / Approvals / drafts at the database). So teardown
// clears the things that point at eval users, deletes the eval trips (letting the
// DB cascade the subtrees), then deletes the users (Identity join tables cascade),
// and finally the batch's namespaced reference data.
//
// Steps 1-5 also sweep any stray rows that reference an eval user from OUTSIDE the
// batch's own trips (e.g. an eval arranger who entered an expense on a real trip).
// In a self-contained eval world these are usually no-ops, but they make the user
// delete in step 7 impossible to block.
//
// Runs in a single transaction: it all commits or none of it does. Requires an
// explicit, non-empty batch id, so it can never delete "everything".
public static class EvalTeardown
{
    public sealed record Result(int Users, int Trips)
    {
        public override string ToString() => $"{Users} user(s), {Trips} trip(s) (plus cascaded rows)";
    }

    public static async Task<Result> RemoveAsync(AppDbContext db, string batchId)
    {
        EvalNaming.ValidateBatchId(batchId);

        var evalUserIds = await db.Users
            .Where(u => u.EvalBatchId == batchId)
            .Select(u => u.Id)
            .ToListAsync();

        var tripCount = await db.Trips.CountAsync(t => t.EvalBatchId == batchId);

        var codePrefix = EvalNaming.CodePrefix(batchId);
        var deptPrefix = EvalNaming.DeptPrefix(batchId);

        await using var tx = await db.Database.BeginTransactionAsync();

        // 1. Break approver back-references pointing INTO the batch (from eval or,
        //    defensively, any real user) so the user delete in step 7 isn't blocked.
        if (evalUserIds.Count > 0)
        {
            await db.Users
                .Where(u => u.ApproverId != null && evalUserIds.Contains(u.ApproverId))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ApproverId, u => (string?)null));

            // 2-5. Sweep audit-owned rows that reference an eval user but live on a
            //      trip outside this batch (Restrict FKs). Expense/Mileage deletes
            //      cascade their splits/waypoints at the DB.
            await db.Approvals
                .Where(a => evalUserIds.Contains(a.ApproverId))
                .ExecuteDeleteAsync();

            await db.Expenses
                .Where(e => e.CreatedById != null && evalUserIds.Contains(e.CreatedById))
                .ExecuteDeleteAsync();

            await db.MileageEntries
                .Where(m => m.CreatedById != null && evalUserIds.Contains(m.CreatedById))
                .ExecuteDeleteAsync();

            await db.ExpenseDrafts
                .Where(d => evalUserIds.Contains(d.UserId))
                .ExecuteDeleteAsync();

            await db.ArrangerAssignments
                .Where(a => evalUserIds.Contains(a.ArrangerId) || evalUserIds.Contains(a.TravelerId))
                .ExecuteDeleteAsync();
        }

        // 6. Delete the batch's trips. Matches the EvalBatchId tag plus, defensively,
        //    any trip whose traveler/creator is an eval user. DB cascade removes
        //    Destinations, Expenses (+splits), Mileage (+waypoints), Approvals, drafts.
        await db.Trips
            .Where(t => t.EvalBatchId == batchId
                        || evalUserIds.Contains(t.TravelerId)
                        || (t.CreatedById != null && evalUserIds.Contains(t.CreatedById)))
            .ExecuteDeleteAsync();

        // 7. Delete the eval users. Identity's own join tables (roles, claims,
        //    logins, tokens) are cascade-deleted by the DB.
        await db.Users
            .Where(u => u.EvalBatchId == batchId)
            .ExecuteDeleteAsync();

        // 8. Batch-namespaced reference data. Safe last: their FKs are SetNull, and
        //    everything that referenced them is already gone.
        await db.CostCenters
            .Where(c => c.Code.StartsWith(codePrefix))
            .ExecuteDeleteAsync();

        await db.ProjectCodes
            .Where(p => p.Code.StartsWith(codePrefix))
            .ExecuteDeleteAsync();

        await db.Departments
            .Where(d => d.Name.StartsWith(deptPrefix))
            .ExecuteDeleteAsync();

        await tx.CommitAsync();

        return new Result(evalUserIds.Count, tripCount);
    }
}
