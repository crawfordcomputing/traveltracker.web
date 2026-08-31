namespace TravelTracker.Web.Domain;

// Resolves the *effective* approver of a traveler: the person a submitted trip is
// routed to. A user's own AppUser.ApproverId always wins; when it is unset, the
// trip falls back to the traveler's department default (Department.DefaultApproverId).
//
// Pure and DB-free so it can be unit-tested and reused wherever routing is decided
// (trip submit, the approver-inbox queries, the "can this user decide" check). The
// equivalent expression is inlined into the EF queries in ApprovalInbox because a
// method call can't be translated to SQL; keep the two in step.
public static class ApproverResolution
{
    // The effective approver id, or null when the trip cannot be routed:
    //   - neither a personal approver nor a department default is set, or
    //   - the only candidate is the traveler themselves (a user can never approve
    //     their own trip; this guards the department-default case where a member is
    //     their own department's default).
    public static string? EffectiveApproverId(
        string travelerId, string? personalApproverId, string? departmentDefaultApproverId)
    {
        ArgumentNullException.ThrowIfNull(travelerId);

        var id = !string.IsNullOrEmpty(personalApproverId)
            ? personalApproverId
            : (string.IsNullOrEmpty(departmentDefaultApproverId) ? null : departmentDefaultApproverId);

        return id == travelerId ? null : id;
    }
}
