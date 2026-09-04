using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// The IEmailSender decorator that writes one NotificationLog row per send attempt.
// Runs against a real (LocalDB) database because the persist-failure case relies on
// the column length constraint actually being enforced.
public class AuditingEmailSenderTests
{
    private sealed class FakeSender : IEmailSender
    {
        public Exception? Throw { get; init; }
        public int Calls { get; private set; }
        public Task SendAsync(string recipient, string subject, string htmlBody)
        {
            Calls++;
            return Throw is null ? Task.CompletedTask : Task.FromException(Throw);
        }
    }

    private static AppDbContext NewDb(string cs)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AuditingEmailSender Sut(IEmailSender inner, AppDbContext db) =>
        new(inner, db, NullLogger<AuditingEmailSender>.Instance);

    [Fact]
    public async Task Successful_Send_Records_Sent_Row_Without_Body()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            var inner = new FakeSender();

            await Sut(inner, db).SendAsync("to@example.com", "Hello", "<p>secret link</p>");

            var row = Assert.Single(await db.NotificationLogs.AsNoTracking().ToListAsync());
            Assert.Equal(1, inner.Calls);
            Assert.Equal(NotificationStatus.Sent, row.Status);
            Assert.Equal("to@example.com", row.Recipient);
            Assert.Equal("Hello", row.Subject);
            Assert.Null(row.Error);
        }
        finally { TestDatabase.Drop(cs); }
    }

    [Fact]
    public async Task Failed_Send_Records_Failed_Row_And_Rethrows()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            var inner = new FakeSender { Throw = new InvalidOperationException("smtp down") };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Sut(inner, db).SendAsync("to@example.com", "Hello", "<p>x</p>"));
            Assert.Equal("smtp down", ex.Message);

            var row = Assert.Single(await db.NotificationLogs.AsNoTracking().ToListAsync());
            Assert.Equal(NotificationStatus.Failed, row.Status);
            Assert.Equal("smtp down", row.Error);
        }
        finally { TestDatabase.Drop(cs); }
    }

    [Fact]
    public async Task Persist_Failure_Is_Swallowed_And_Does_Not_Poison_The_Context()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            var inner = new FakeSender();

            // Recipient exceeds the nvarchar(256) column, so SaveChanges fails inside
            // RecordAsync. The send itself must still succeed for the caller...
            var tooLong = new string('a', 300) + "@example.com";
            await Sut(inner, db).SendAsync(tooLong, "Hello", "<p>x</p>");
            Assert.Equal(1, inner.Calls);

            // ...the failed row must be detached (nothing left tracked)...
            Assert.Empty(db.ChangeTracker.Entries());

            // ...and a later, unrelated save on the same context must work.
            db.Departments.Add(new Department { Name = "Ops" });
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.Departments.CountAsync());
            Assert.Equal(0, await db.NotificationLogs.CountAsync());
        }
        finally { TestDatabase.Drop(cs); }
    }
}
