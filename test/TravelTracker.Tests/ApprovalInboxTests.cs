using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using Xunit;

namespace TravelTracker.Tests;

// The approver's queue must agree with Domain/ApproverResolution: a personal
// ApproverId wins, else the department default applies, and a user is never their
// own effective approver.
public class ApprovalInboxTests
{
    private static AppDbContext NewDb(string cs)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AppUser User(string id, int? deptId = null, string? approverId = null) => new()
    {
        Id = id, UserName = $"{id}@example.com", Email = $"{id}@example.com",
        DisplayName = id, DepartmentId = deptId, ApproverId = approverId
    };

    private static Trip Submitted(string travelerId) => new()
    {
        TravelerId = travelerId, Purpose = "Trip", Status = TripStatus.Submitted,
        StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 3)
    };

    [Fact]
    public async Task Department_Default_Routes_Members_Without_Personal_Approver()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            db.Users.AddRange(User("boss"), User("mgr"));
            await db.SaveChangesAsync();
            var dept = new Department { Name = "Sales", DefaultApproverId = "boss" };
            db.Departments.Add(dept);
            await db.SaveChangesAsync();
            db.Users.AddRange(
                User("alice", dept.Id),               // falls back to boss
                User("bob", dept.Id, approverId: "mgr")); // personal approver wins
            db.Trips.AddRange(Submitted("alice"), Submitted("bob"));
            await db.SaveChangesAsync();

            var inbox = new ApprovalInbox(db);
            Assert.True(await inbox.IsApproverAsync("boss"));
            Assert.Equal(1, await inbox.PendingCountAsync("boss"));
            Assert.Equal("alice", Assert.Single(await inbox.PendingAsync("boss")).TravelerId);

            Assert.Equal(1, await inbox.PendingCountAsync("mgr"));
            Assert.Equal("bob", Assert.Single(await inbox.PendingAsync("mgr")).TravelerId);
        }
        finally { TestDatabase.Drop(cs); }
    }

    [Fact]
    public async Task User_Is_Never_Their_Own_Effective_Approver()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            db.Users.Add(User("boss"));
            await db.SaveChangesAsync();
            // boss is a member of the department whose default approver is boss.
            var dept = new Department { Name = "Exec", DefaultApproverId = "boss" };
            db.Departments.Add(dept);
            await db.SaveChangesAsync();
            db.Users.Find("boss")!.DepartmentId = dept.Id;
            db.Trips.Add(Submitted("boss"));
            await db.SaveChangesAsync();

            var inbox = new ApprovalInbox(db);
            Assert.False(await inbox.IsApproverAsync("boss"));
            Assert.Equal(0, await inbox.PendingCountAsync("boss"));
            Assert.Empty(await inbox.PendingAsync("boss"));
        }
        finally { TestDatabase.Drop(cs); }
    }
}
