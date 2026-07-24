using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class MileageRateResolverTests
{
    // Mirrors the seeded US business-auto history, including the 2026 mid-year jump.
    private static List<MileageRate> UsRates() => new()
    {
        new() { Id = 1, EffectiveDate = new(2024, 1, 1), Jurisdiction = "US", VehicleType = VehicleType.Car, Unit = DistanceUnit.Miles, Rate = 0.670m },
        new() { Id = 2, EffectiveDate = new(2025, 1, 1), Jurisdiction = "US", VehicleType = VehicleType.Car, Unit = DistanceUnit.Miles, Rate = 0.700m },
        new() { Id = 3, EffectiveDate = new(2026, 1, 1), Jurisdiction = "US", VehicleType = VehicleType.Car, Unit = DistanceUnit.Miles, Rate = 0.725m },
        new() { Id = 4, EffectiveDate = new(2026, 7, 1), Jurisdiction = "US", VehicleType = VehicleType.Car, Unit = DistanceUnit.Miles, Rate = 0.760m },
    };

    private static MileageRate? Resolve(DateOnly date) =>
        MileageRateResolver.Resolve(UsRates(), "US", VehicleType.Car, DistanceUnit.Miles, date);

    [Fact]
    public void Picks_Latest_Rate_On_Or_Before_The_Date()
        => Assert.Equal(0.700m, Resolve(new(2025, 6, 15))!.Rate);

    [Fact]
    public void Effective_Date_Boundary_Is_Inclusive()
        => Assert.Equal(0.725m, Resolve(new(2026, 1, 1))!.Rate);

    [Fact]
    public void Resolves_First_Half_2026_Before_The_Mid_Year_Jump()
        => Assert.Equal(0.725m, Resolve(new(2026, 6, 30))!.Rate);

    [Fact]
    public void Resolves_Second_Half_2026_After_The_Mid_Year_Jump()
        => Assert.Equal(0.760m, Resolve(new(2026, 7, 1))!.Rate);

    [Fact]
    public void Returns_Null_When_No_Rate_Yet_In_Effect()
        => Assert.Null(Resolve(new(2020, 1, 1)));

    [Fact]
    public void Filters_On_Jurisdiction()
        => Assert.Null(MileageRateResolver.Resolve(UsRates(), "UK", VehicleType.Car, DistanceUnit.Miles, new(2026, 1, 1)));

    [Fact]
    public void Filters_On_Vehicle_Type()
        => Assert.Null(MileageRateResolver.Resolve(UsRates(), "US", VehicleType.Motorcycle, DistanceUnit.Miles, new(2026, 1, 1)));

    [Fact]
    public void Filters_On_Unit()
        => Assert.Null(MileageRateResolver.Resolve(UsRates(), "US", VehicleType.Car, DistanceUnit.Kilometers, new(2026, 1, 1)));

    [Fact]
    public void Jurisdiction_Match_Is_Case_Insensitive()
        => Assert.Equal(0.725m, MileageRateResolver.Resolve(UsRates(), "us", VehicleType.Car, DistanceUnit.Miles, new(2026, 1, 1))!.Rate);
}
