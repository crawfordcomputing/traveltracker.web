namespace TravelTracker.Web.Data.Entities;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Fallback approver for members of this department who have no personal
    // ApproverId. Lets an admin set one approver per department instead of touching
    // every user, and keeps new hires submittable the moment their department is
    // set. A user's own ApproverId always wins (see Domain/ApproverResolution).
    // Optional, self-references AppUser, Restrict on delete like every AppUser FK.
    public string? DefaultApproverId { get; set; }
    public AppUser? DefaultApprover { get; set; }
}
