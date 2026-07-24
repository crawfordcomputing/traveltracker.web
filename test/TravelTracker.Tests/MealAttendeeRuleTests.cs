using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class MealAttendeeRuleTests
{
    [Theory]
    [InlineData(ExpenseCategory.Meals, true)]
    [InlineData(ExpenseCategory.Entertainment, true)]
    [InlineData(ExpenseCategory.Lodging, false)]
    [InlineData(ExpenseCategory.Airfare, false)]
    [InlineData(ExpenseCategory.Unspecified, false)]
    public void RequiresAttendees_Only_For_Meals_And_Entertainment(ExpenseCategory category, bool expected)
        => Assert.Equal(expected, MealAttendeeRule.RequiresAttendees(category));

    [Fact]
    public void Incomplete_When_Meal_Missing_Either_Field()
    {
        Assert.True(MealAttendeeRule.IsIncomplete(ExpenseCategory.Meals, null, "Client dinner"));
        Assert.True(MealAttendeeRule.IsIncomplete(ExpenseCategory.Meals, "Jane, Bob", "  "));
    }

    [Fact]
    public void Complete_When_Meal_Has_Both_Fields()
        => Assert.False(MealAttendeeRule.IsIncomplete(ExpenseCategory.Meals, "Jane, Bob", "Client dinner"));

    [Fact]
    public void Non_Meal_Is_Never_Incomplete()
        => Assert.False(MealAttendeeRule.IsIncomplete(ExpenseCategory.Lodging, null, null));
}
