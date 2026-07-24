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
}
