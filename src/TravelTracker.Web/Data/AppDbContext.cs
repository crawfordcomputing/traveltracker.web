using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Data;

// Identity-backed context. AppUser is the Identity user; roles use the default
// IdentityRole (string key). Domain tables (Department, Trips, Destinations)
// live here too.
//
// DateTimeOffset columns map to SQL Server's native datetimeoffset type, which
// supports ORDER BY and range filters directly — no value conversion needed.
public class AppDbContext : IdentityDbContext<AppUser, IdentityRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Fixed timestamp for HasData seed rows. MUST be a constant, not DateTimeOffset.Now:
    // a moving value makes EF see a model change on every build (endless migration churn).
    private static readonly DateTimeOffset SeedStamp = new(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseSplit> ExpenseSplits => Set<ExpenseSplit>();
    public DbSet<ExpenseDraft> ExpenseDrafts => Set<ExpenseDraft>();
    public DbSet<MileageEntry> MileageEntries => Set<MileageEntry>();
    public DbSet<MileageWaypoint> MileageWaypoints => Set<MileageWaypoint>();
    public DbSet<MileageRate> MileageRates => Set<MileageRate>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<UsState> UsStates => Set<UsState>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ArrangerAssignment> ArrangerAssignments => Set<ArrangerAssignment>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<ProjectCode> ProjectCodes => Set<ProjectCode>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<ExpensePolicy> ExpensePolicies => Set<ExpensePolicy>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // configures Identity tables — must run first

        builder.Entity<Department>()
            .HasIndex(d => d.Name).IsUnique();

        // Department-level fallback approver (see Department.DefaultApproverId).
        // Restrict on delete, matching every other AppUser FK: no hard-delete path,
        // and Restrict avoids the multiple-cascade-path error on SQL Server. Neither
        // this FK nor AppUser.DepartmentId (SetNull) cascades, so the two-way
        // AppUser<->Department reference is safe.
        builder.Entity<Department>()
            .HasOne(d => d.DefaultApprover)
            .WithMany()
            .HasForeignKey(d => d.DefaultApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Country>().HasIndex(c => c.Code).IsUnique();
        builder.Entity<UsState>().HasIndex(s => s.Code).IsUnique();

        // Cost centers / project codes are admin-managed reference data with a
        // globally unique Code (mirrors Country/UsState).
        builder.Entity<CostCenter>().HasIndex(c => c.Code).IsUnique();
        builder.Entity<ProjectCode>().HasIndex(p => p.Code).IsUnique();

        builder.Entity<Invitation>(e =>
        {
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.HasIndex(i => i.Email);
            // Department is reference data; clear the link if a department is
            // deleted rather than blocking or cascading.
            e.HasOne(i => i.Department)
                .WithMany()
                .HasForeignKey(i => i.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AppUser>()
            .HasOne(u => u.Department)
            .WithMany()
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Default cost center is reference data; clear the link if the cost center
        // is retired-and-deleted rather than blocking or cascading (as Department).
        builder.Entity<AppUser>()
            .HasOne(u => u.DefaultCostCenter)
            .WithMany()
            .HasForeignKey(u => u.DefaultCostCenterId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-referencing approver relationship (who approves this user's trips).
        // Restrict, matching every other AppUser FK: there is no hard-delete path,
        // and Restrict avoids the multiple-cascade-path error on SQL Server. The
        // relationship is kept acyclic in the admin UI via Domain/ApproverGraph.
        builder.Entity<AppUser>()
            .HasOne(u => u.Approver)
            .WithMany()
            .HasForeignKey(u => u.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        // Filtered index over the eval-batch tag: only eval rows are indexed, so
        // teardown's "delete where EvalBatchId = @batch" is cheap and real-world
        // queries carry no extra index weight. Mirrored on Trip below.
        builder.Entity<AppUser>()
            .HasIndex(u => u.EvalBatchId)
            .HasFilter("[EvalBatchId] IS NOT NULL");

        builder.Entity<Trip>(e =>
        {
            // Restrict on the user FKs: deleting a user should never silently
            // cascade-delete their trip history, and Restrict avoids the multiple
            // cascade-path error on SQL Server.
            e.HasOne(t => t.Traveler)
                .WithMany()
                .HasForeignKey(t => t.TravelerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Destinations are owned by the trip — deleting a trip removes its legs.
            e.HasMany(t => t.Destinations)
                .WithOne(d => d.Trip!)
                .HasForeignKey(d => d.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cost center / project code are reference data; SetNull so retiring a
            // code leaves the trip's history intact rather than blocking a delete.
            e.HasOne(t => t.CostCenter)
                .WithMany()
                .HasForeignKey(t => t.CostCenterId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(t => t.ProjectCode)
                .WithMany()
                .HasForeignKey(t => t.ProjectCodeId)
                .OnDelete(DeleteBehavior.SetNull);

            // Filtered index over the eval-batch tag (see AppUser above). Deleting
            // an eval trip cascades its Destinations/Expenses/Mileage/Approvals.
            e.HasIndex(t => t.EvalBatchId)
                .HasFilter("[EvalBatchId] IS NOT NULL");
        });

        builder.Entity<Expense>(e =>
        {
            // Expenses are owned by the trip — deleting a trip removes its lines
            // (same as Destinations).
            e.HasOne(x => x.Trip)
                .WithMany()
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict on the audit user FK: deleting a user must never cascade
            // away expense history, and Restrict avoids the multiple cascade-path
            // error on SQL Server.
            e.HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Money columns: SQL Server needs explicit precision (SQLite ignores
            // it). 18,2 for amounts, 18,6 for the FX rate.
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BaseAmount).HasPrecision(18, 2);
            e.Property(x => x.FxRate).HasPrecision(18, 6);

            e.HasIndex(x => x.TripId);

            // Category splits are owned by the parent expense — deleting the expense
            // removes its splits (same ownership model as Destinations/Waypoints).
            e.HasMany(x => x.Splits)
                .WithOne(s => s.Expense!)
                .HasForeignKey(s => s.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExpenseSplit>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BaseAmount).HasPrecision(18, 2);

            e.HasIndex(x => x.ExpenseId);
        });

        builder.Entity<ExpenseDraft>(e =>
        {
            // One live draft per user per trip; the JS autosave upserts on this key.
            e.HasIndex(x => new { x.UserId, x.TripId }).IsUnique();

            e.Property(x => x.Amount).HasPrecision(18, 2);

            // Trip-owned: if the trip is deleted, its scratch drafts go too.
            e.HasOne(x => x.Trip)
                .WithMany()
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict on the user FK, matching every other AppUser FK — no
            // cascade, dodges SQL Server's multiple-cascade-path error.
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExpensePolicy>(e =>
        {
            // One guideline per category. Absence of a row means "uncapped".
            e.HasIndex(x => x.Category).IsUnique();

            // Base-currency money column: explicit precision for SQL Server.
            e.Property(x => x.CapAmount).HasPrecision(18, 2);

            // Seed the defaults that ExpensePolicyCheck used to hardcode, so the
            // over-guideline badge behaves identically the moment this ships. Admins
            // can then edit/delete these under /Admin/ExpensePolicies. Fixed Ids are
            // required by HasData. See ADR-0003.
            e.HasData(
                new ExpensePolicy { Id = 1, Category = ExpenseCategory.Meals, CapAmount = 75m, UpdatedAt = SeedStamp },
                new ExpensePolicy { Id = 2, Category = ExpenseCategory.Lodging, CapAmount = 350m, UpdatedAt = SeedStamp },
                new ExpensePolicy { Id = 3, Category = ExpenseCategory.Entertainment, CapAmount = 150m, UpdatedAt = SeedStamp },
                new ExpensePolicy { Id = 4, Category = ExpenseCategory.GroundTransport, CapAmount = 100m, UpdatedAt = SeedStamp }
            );
        });

        builder.Entity<MileageRate>(e =>
        {
            // One rate per (jurisdiction, vehicle, unit) that takes effect on a
            // given date; a later EffectiveDate for the same key supersedes it.
            e.HasIndex(r => new { r.Jurisdiction, r.VehicleType, r.Unit, r.EffectiveDate })
                .IsUnique();

            e.Property(r => r.Rate).HasPrecision(18, 6);
        });

        builder.Entity<MileageEntry>(e =>
        {
            // Mileage entries are owned by the trip — deleting a trip removes its
            // lines (same as Destination / Expense).
            e.HasOne(x => x.Trip)
                .WithMany()
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            // Audit link to the source rate row: SetNull so removing a rate leaves
            // the entry (with its frozen Rate/Amount) intact rather than blocking or
            // cascade-deleting reimbursement history.
            e.HasOne(x => x.MileageRate)
                .WithMany()
                .HasForeignKey(x => x.RateId)
                .OnDelete(DeleteBehavior.SetNull);

            // Restrict on the audit user FK, matching Trip/Expense — deleting a user
            // must never cascade away mileage history (dodges SQL Server multi-path).
            e.HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Waypoints are owned by the entry.
            e.HasMany(x => x.Waypoints)
                .WithOne(w => w.MileageEntry!)
                .HasForeignKey(w => w.MileageEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Money/distance columns need explicit SQL Server precision (SQLite
            // ignores it). 18,2 for the amount, 18,6 for the rate, 18,3 for distances.
            e.Property(x => x.Distance).HasPrecision(18, 3);
            e.Property(x => x.CommuteDeduction).HasPrecision(18, 3);
            e.Property(x => x.Rate).HasPrecision(18, 6);
            e.Property(x => x.Amount).HasPrecision(18, 2);

            e.HasIndex(x => x.TripId);
        });

        builder.Entity<Approval>(e =>
        {
            // Approval rows are owned by the trip — deleting a trip removes its
            // decision history (same ownership model as Destinations/Expenses).
            e.HasOne(a => a.Trip)
                .WithMany(t => t.Approvals)
                .HasForeignKey(a => a.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict on the approver user FK, matching every other AppUser FK:
            // deleting a user must never cascade away approval history, and Restrict
            // dodges SQL Server's multiple-cascade-path error.
            e.HasOne(a => a.Approver)
                .WithMany()
                .HasForeignKey(a => a.ApproverId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => a.TripId);
        });

        builder.Entity<NotificationLog>(e =>
        {
            // Admin view is "newest first, optionally filtered by status".
            e.HasIndex(x => x.SentAt);
            e.HasIndex(x => x.Status);
        });

        builder.Entity<ArrangerAssignment>(e =>
        {
            // One row per (arranger, traveler) pair.
            e.HasIndex(a => new { a.ArrangerId, a.TravelerId }).IsUnique();

            // Restrict on both user FKs, matching Trip → AppUser: deleting a user
            // must never silently cascade, and Restrict avoids the multiple
            // cascade-path error on SQL Server. Rows are removed via the admin UI.
            e.HasOne(a => a.Arranger)
                .WithMany()
                .HasForeignKey(a => a.ArrangerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.Traveler)
                .WithMany()
                .HasForeignKey(a => a.TravelerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
