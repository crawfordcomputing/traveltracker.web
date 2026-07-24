namespace TravelTracker.Web.Data.Entities;

// Canonical role names used across seeding and authorization policies.
public static class Roles
{
    public const string Employee = "Employee";
    public const string Arranger = "Arranger";   // travel arranger / delegate: acts only for assigned travelers (scoped)
    public const string Manager = "Manager";
    public const string Finance = "Finance";     // sees expenses/reports (M3/M4), not account god-mode
    public const string Admin = "Admin";

    public static readonly string[] All = { Employee, Arranger, Manager, Finance, Admin };
}
