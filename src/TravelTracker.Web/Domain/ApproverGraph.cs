namespace TravelTracker.Web.Domain;

// Pure, testable rules for the AppUser.ApproverId self-reference. Assigning an
// approver must never make a user their own approver (directly or transitively):
// the approver chain has to stay acyclic so the M6 approval flow can walk "who
// approves this trip" without looping forever.
//
// Kept free of EF/Identity so it can be unit-tested with a plain dictionary and
// reused by any surface that assigns an approver (Admin/Users Edit today, the M6
// approver UI later).
public static class ApproverGraph
{
    // True if setting userId's approver to approverId would create a cycle —
    // i.e. userId already sits somewhere on approverId's own approver chain, or
    // approverId == userId (self-approval).
    //
    // approverOf resolves a user's *current* approver id (null when unassigned).
    // The proposed edge is not yet persisted, so we start from approverId and walk
    // upward using the existing chain. A visited set guards against a pre-existing
    // cycle in the data (shouldn't happen, but never loop forever).
    public static bool CreatesCycle(string userId, string? approverId, Func<string, string?> approverOf)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(approverOf);

        if (string.IsNullOrEmpty(approverId))
            return false; // clearing the approver can never create a cycle

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = approverId;
        while (!string.IsNullOrEmpty(current))
        {
            if (current == userId)
                return true;              // the chain leads back to the user -> cycle
            if (!visited.Add(current))
                return true;              // hit an existing loop -> treat as a cycle
            current = approverOf(current);
        }
        return false;
    }

    // Convenience overload for callers that already hold the whole map in memory
    // (userId -> approverId). Missing keys resolve to null (unassigned).
    public static bool CreatesCycle(
        string userId, string? approverId, IReadOnlyDictionary<string, string?> approverByUser)
    {
        ArgumentNullException.ThrowIfNull(approverByUser);
        return CreatesCycle(userId, approverId,
            id => approverByUser.TryGetValue(id, out var a) ? a : null);
    }

    // Why a user was left out of a bulk approver assignment.
    public enum BulkSkipReason
    {
        // The user was the approver being assigned (can't approve their own trips).
        SelfApproval,
        // Assigning this approver would put the user on their own approver chain.
        WouldCreateCycle,
    }

    // Plans a "set this one approver on all these users" action without touching the
    // database: decides which users can take the approver and which must be skipped.
    // approverByUser is the CURRENT approver map and is MUTATED as assignments are
    // planned, so two selected users that form a chain (A picks B while B is being
    // pointed at A) are still caught within a single batch. Order follows userIds.
    public static (List<string> ToAssign, List<(string UserId, BulkSkipReason Reason)> Skipped)
        PlanBulkApproverAssignment(
            string approverId, IEnumerable<string> userIds,
            Dictionary<string, string?> approverByUser)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentNullException.ThrowIfNull(approverByUser);

        var toAssign = new List<string>();
        var skipped = new List<(string, BulkSkipReason)>();
        foreach (var userId in userIds)
        {
            if (userId == approverId)
            {
                skipped.Add((userId, BulkSkipReason.SelfApproval));
                continue;
            }
            if (CreatesCycle(userId, approverId, approverByUser))
            {
                skipped.Add((userId, BulkSkipReason.WouldCreateCycle));
                continue;
            }
            approverByUser[userId] = approverId; // reflect the planned edge for later rows
            toAssign.Add(userId);
        }
        return (toAssign, skipped);
    }
}
