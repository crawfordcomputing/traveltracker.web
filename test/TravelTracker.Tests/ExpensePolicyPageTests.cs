using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Pages.Admin.ExpensePolicies;
using Xunit;

namespace TravelTracker.Tests;

// Handlers on /Admin/ExpensePolicies. The seeded defaults (Meals/Lodging/
// Entertainment/GroundTransport) come from AppDbContext.HasData via EnsureCreated.
public class ExpensePolicyPageTests
{
    private static AppDbContext NewDb(string cs)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Update_Changes_Cap_And_Notes()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            var meals = await db.ExpensePolicies.SingleAsync(p => p.Category == ExpenseCategory.Meals);

            var result = await new IndexModel(db).OnPostUpdateAsync(meals.Id, 90m, "  per day ");

            Assert.IsType<RedirectToPageResult>(result);
            var saved = await db.ExpensePolicies.AsNoTracking().SingleAsync(p => p.Id == meals.Id);
            Assert.Equal(90m, saved.CapAmount);
            Assert.Equal("per day", saved.Notes);
        }
        finally { TestDatabase.Drop(cs); }
    }

    [Theory]
    [InlineData(null)]        // blank / non-numeric field binds to null, must not become 0
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public async Task Update_Rejects_Missing_Or_Out_Of_Range_Cap(int? amount)
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            var meals = await db.ExpensePolicies.SingleAsync(p => p.Category == ExpenseCategory.Meals);
            var page = new IndexModel(db);

            var result = await page.OnPostUpdateAsync(meals.Id, amount, null);

            Assert.IsType<PageResult>(result);
            Assert.False(page.ModelState.IsValid);
            var saved = await db.ExpensePolicies.AsNoTracking().SingleAsync(p => p.Id == meals.Id);
            Assert.Equal(75m, saved.CapAmount); // seeded value untouched
        }
        finally { TestDatabase.Drop(cs); }
    }

    [Fact]
    public async Task Create_Rejects_Duplicate_Category()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            var page = new IndexModel(db);
            page.Input = new IndexModel.InputModel { Category = ExpenseCategory.Meals, CapAmount = 10m };

            var result = await page.OnPostCreateAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(page.ModelState.IsValid);
            Assert.Equal(1, await db.ExpensePolicies.CountAsync(p => p.Category == ExpenseCategory.Meals));
        }
        finally { TestDatabase.Drop(cs); }
    }

    [Fact]
    public async Task Delete_Makes_Category_Uncapped()
    {
        var cs = TestDatabase.NewConnectionString();
        try
        {
            await using var db = NewDb(cs);
            var meals = await db.ExpensePolicies.SingleAsync(p => p.Category == ExpenseCategory.Meals);

            await new IndexModel(db).OnPostDeleteAsync(meals.Id);

            Assert.False(await db.ExpensePolicies.AnyAsync(p => p.Category == ExpenseCategory.Meals));
        }
        finally { TestDatabase.Drop(cs); }
    }
}
