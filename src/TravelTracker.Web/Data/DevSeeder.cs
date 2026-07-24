#if DEBUG
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Data;

// Development-only sample data: extra departments, a user in every role, arranger
// delegations, and a spread of trips/itineraries so the UI has something to show.
//
// Compiled ONLY in Debug builds (#if DEBUG): the Release/production artifact does
// not contain this class at all. Program.cs gates the call the same way, so the
// dev seed and its test fixtures never ship in the prod DLL.
//
// NEVER runs outside Development even in Debug: Program.cs also gates the call on
// IHostEnvironment.IsDevelopment() AND the Seed:DevData config flag (true in
// appsettings.Development.json; integration tests set it false to opt out).
// Idempotent: every user/department/trip is keyed on a natural identifier and
// skipped if it already exists, so it's safe to run on every dev startup. Baseline
// data (roles, the "General" department, the admin) is owned by DbInitializer and
// assumed to have already run.
public static class DevSeeder
{
    // Shared throwaway password for every seeded account. Dev-only; the login page
    // shows nothing about it, so it's documented in tasks/todo.md instead.
    public const string Password = "Password123!";

    private sealed record SeedUser(
        string Email, string DisplayName, string Role, string Department, string BaseLocation);

    private static readonly string[] Departments =
        { "Engineering", "Sales", "Finance", "Operations", "Executive" };

    private static readonly (string Code, string Name)[] CostCentersSeed =
    {
        ("ENG-100", "Engineering — Platform"),
        ("SAL-200", "Sales — Field"),
        ("OPS-300", "Operations"),
        ("EXE-900", "Executive"),
    };

    private static readonly (string Code, string Name)[] ProjectCodesSeed =
    {
        ("PRJ-2026-01", "Q1 Customer Onsites"),
        ("PRJ-2026-04", "Q2 Field Rollout"),
        ("INT-INFRA",   "Internal Infrastructure"),
    };

    private static readonly SeedUser[] Users =
    {
        new("manager@example.com",  "Morgan Bailey",  Roles.Manager,  "Engineering", "Seattle, WA"),
        new("finance@example.com",  "Fiona Chen",     Roles.Finance,  "Finance",     "New York, NY"),
        new("arranger@example.com", "Ava Delgado",    Roles.Arranger, "Operations",  "Chicago, IL"),
        new("arranger2@example.com","Alex Rivera",    Roles.Arranger, "Executive",   "Austin, TX"),
        new("evan@example.com",     "Evan Parker",    Roles.Employee, "Engineering", "Seattle, WA"),
        new("ella@example.com",     "Ella Nguyen",    Roles.Employee, "Sales",       "Denver, CO"),
        new("ethan@example.com",    "Ethan Brooks",   Roles.Employee, "Engineering", "Seattle, WA"),
        new("emma@example.com",     "Emma Sorensen",  Roles.Employee, "Operations",  "Chicago, IL"),
    };

    // (arranger email -> traveler emails they may act for)
    private static readonly (string Arranger, string[] Travelers)[] Delegations =
    {
        ("arranger@example.com",  new[] { "ethan@example.com", "emma@example.com" }),
        ("arranger2@example.com", new[] { "evan@example.com" }),
    };

    // (user email -> approver email). Seats the reports-to/approver relationship
    // (AppUser.ApproverId) the M6 approval flow builds on. Intentionally a two-level
    // chain (employees -> Morgan -> Alex) so the graph isn't flat. Not the same as
    // arranger delegation: an approver signs off, an arranger acts on someone's behalf.
    private static readonly (string User, string Approver)[] Approvers =
    {
        ("evan@example.com",     "manager@example.com"),
        ("ethan@example.com",    "manager@example.com"),
        ("ella@example.com",     "manager@example.com"),
        ("emma@example.com",     "manager@example.com"),
        ("manager@example.com",  "arranger2@example.com"),
        ("finance@example.com",  "arranger2@example.com"),
    };

    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<AppUser> userManager)
    {
        // --- Departments -----------------------------------------------------
        foreach (var name in Departments)
        {
            if (!await db.Departments.AnyAsync(d => d.Name == name))
                db.Departments.Add(new Department { Name = name });
        }
        await db.SaveChangesAsync();

        var deptByName = await db.Departments.ToDictionaryAsync(d => d.Name, d => d.Id);

        // --- Cost centers / project codes (M5 reference data) ----------------
        foreach (var (code, name) in CostCentersSeed)
        {
            if (!await db.CostCenters.AnyAsync(c => c.Code == code))
                db.CostCenters.Add(new CostCenter { Code = code, Name = name, IsActive = true });
        }
        foreach (var (code, name) in ProjectCodesSeed)
        {
            if (!await db.ProjectCodes.AnyAsync(p => p.Code == code))
                db.ProjectCodes.Add(new ProjectCode { Code = code, Name = name, IsActive = true });
        }
        await db.SaveChangesAsync();

        var costCenterByCode = await db.CostCenters.ToDictionaryAsync(c => c.Code, c => c.Id);
        var projectCodeByCode = await db.ProjectCodes.ToDictionaryAsync(p => p.Code, p => p.Id);

        // --- Users -----------------------------------------------------------
        var userByEmail = new Dictionary<string, AppUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in Users)
        {
            var user = await userManager.FindByEmailAsync(seed.Email);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = seed.Email,
                    Email = seed.Email,
                    EmailConfirmed = true,
                    DisplayName = seed.DisplayName,
                    BaseLocation = seed.BaseLocation,
                    DepartmentId = deptByName.TryGetValue(seed.Department, out var did) ? did : null,
                    IsActive = true,
                };
                var result = await userManager.CreateAsync(user, Password);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"DevSeeder: failed to create {seed.Email}: " +
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(user, seed.Role);
            }
            userByEmail[seed.Email] = user;
        }

        // --- Arranger delegations -------------------------------------------
        foreach (var (arrangerEmail, travelerEmails) in Delegations)
        {
            var arrangerId = userByEmail[arrangerEmail].Id;
            foreach (var travelerEmail in travelerEmails)
            {
                var travelerId = userByEmail[travelerEmail].Id;
                var exists = await db.ArrangerAssignments
                    .AnyAsync(a => a.ArrangerId == arrangerId && a.TravelerId == travelerId);
                if (!exists)
                    db.ArrangerAssignments.Add(new ArrangerAssignment
                    {
                        ArrangerId = arrangerId,
                        TravelerId = travelerId,
                    });
            }
        }
        await db.SaveChangesAsync();

        // --- Approver relationships -----------------------------------------
        // Only seat an approver when none is set, so a hand-edited assignment in the
        // admin UI survives a reseed.
        foreach (var (userEmail, approverEmail) in Approvers)
        {
            var user = userByEmail[userEmail];
            if (user.ApproverId is null)
            {
                user.ApproverId = userByEmail[approverEmail].Id;
                await userManager.UpdateAsync(user);
            }
        }

        // --- Traveler profile depth -----------------------------------------
        // Only the non-sensitive fields are seeded (no passport/KTN numbers, which
        // would need the encrypting protector). Expiries are spread to exercise the
        // Who's-out duty-of-care warning: one expired, one expiring soon, one fine.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var profiles = new (string Email, DateOnly Expiry, string Nationality, string Mobile)[]
        {
            ("ella@example.com",  today.AddMonths(-2), "United States", "+1 303 555 0101"), // expired
            ("evan@example.com",  today.AddMonths(3),  "United States", "+1 206 555 0102"), // expiring soon
            ("ethan@example.com", today.AddYears(6),   "United States", "+1 206 555 0103"), // fine
        };
        foreach (var (email, expiry, nationality, mobile) in profiles)
        {
            var u = userByEmail[email];
            if (u.PassportExpiry is null)
            {
                u.PassportExpiry = expiry;
                u.Nationality = nationality;
                u.MobileNumber = mobile;
                await userManager.UpdateAsync(u);
            }
        }

        // --- Trips -----------------------------------------------------------
        // Keyed on (TravelerId, Purpose) for idempotency. CreatedById differs from
        // TravelerId on trips booked by an arranger, exercising "on behalf of".
        string Id(string email) => userByEmail[email].Id;

        var trips = new[]
        {
            // Arranger-booked: Alex (arranger2) books for Evan.
            NewTrip(Id("evan@example.com"), Id("arranger2@example.com"),
                "Cloud Summit 2026", TripStatus.Planned, TripType.Conference,
                new(2026, 8, 3), new(2026, 8, 6),
                Leg("Las Vegas", "Nevada", "United States", new(2026, 8, 3), new(2026, 8, 6),
                    TransportMode.Flight, "Aria Resort", 1)),

            // Self-booked upcoming client visit.
            NewTrip(Id("ella@example.com"), Id("ella@example.com"),
                "Q3 client visit — Acme Corp", TripStatus.Planned, TripType.ClientVisit,
                new(2026, 7, 28), new(2026, 7, 30),
                Leg("Austin", "Texas", "United States", new(2026, 7, 28), new(2026, 7, 30),
                    TransportMode.Flight, "Hyatt Regency", 1)),

            // Arranger-booked, completed, multi-leg: Ava books for Ethan.
            NewTrip(Id("ethan@example.com"), Id("arranger@example.com"),
                "Onsite integration work", TripStatus.Completed, TripType.Business,
                new(2026, 6, 15), new(2026, 6, 19),
                Leg("San Jose", "California", "United States", new(2026, 6, 15), new(2026, 6, 17),
                    TransportMode.Flight, "Marriott", 1),
                Leg("Portland", "Oregon", "United States", new(2026, 6, 17), new(2026, 6, 19),
                    TransportMode.Rail, "The Nines", 2)),

            // Draft, international.
            NewTrip(Id("emma@example.com"), Id("emma@example.com"),
                "Operations training", TripStatus.Draft, TripType.Training,
                new(2026, 9, 1), new(2026, 9, 3),
                Leg("Toronto", "Ontario", "Canada", new(2026, 9, 1), new(2026, 9, 3),
                    TransportMode.Flight, "Fairmont Royal York", 1)),

            // Manager's own trip.
            NewTrip(Id("manager@example.com"), Id("manager@example.com"),
                "Leadership offsite", TripStatus.Planned, TripType.Internal,
                new(2026, 8, 12), new(2026, 8, 14),
                Leg("Denver", "Colorado", "United States", new(2026, 8, 12), new(2026, 8, 14),
                    TransportMode.Flight, "The Brown Palace", 1)),

            // Finance user, draft.
            NewTrip(Id("finance@example.com"), Id("finance@example.com"),
                "Year-end audit", TripStatus.Draft, TripType.Business,
                new(2026, 10, 5), new(2026, 10, 7),
                Leg("Boston", "Massachusetts", "United States", new(2026, 10, 5), new(2026, 10, 7),
                    TransportMode.Rail, "Omni Parker House", 1)),
        };

        foreach (var trip in trips)
        {
            var exists = await db.Trips
                .AnyAsync(t => t.TravelerId == trip.TravelerId && t.Purpose == trip.Purpose);
            if (!exists)
                db.Trips.Add(trip);
        }
        await db.SaveChangesAsync();

        // --- Link a cost center / project code onto seeded trips -------------
        // So reports and Details show real reference data in dev. Runs only on the
        // very first seed (when nothing is linked yet) so it never overwrites a
        // cost center a user later cleared on their own trip.
        var engCc = costCenterByCode.GetValueOrDefault("ENG-100");
        var opsCc = costCenterByCode.GetValueOrDefault("OPS-300");
        var q1Proj = projectCodeByCode.GetValueOrDefault("PRJ-2026-01");
        if (engCc != 0 && !await db.Trips.AnyAsync(t => t.CostCenterId != null))
        {
            foreach (var trip in await db.Trips.ToListAsync())
            {
                trip.CostCenterId = engCc;
                if (q1Proj != 0) trip.ProjectCodeId = q1Proj;
            }
            // Seat a default cost center on a couple of travelers so the profile
            // dropdown and Create prefill are visible in dev.
            foreach (var email in new[] { "ethan@example.com", "evan@example.com" })
            {
                if (userByEmail.TryGetValue(email, out var u))
                    u.DefaultCostCenterId = engCc;
            }
            if (opsCc != 0 && userByEmail.TryGetValue("emma@example.com", out var emma))
                emma.DefaultCostCenterId = opsCc;
            await db.SaveChangesAsync();
        }

        // --- Expenses --------------------------------------------------------
        // A spread on Ethan's completed onsite trip so the rollup, personal-flag,
        // and over-guideline badge are all visible. Ava (arranger) entered them on
        // Ethan's behalf (CreatedById != traveler). Idempotent: only seed when the
        // trip has none. Single-currency (M3a): Currency=USD, FxRate=1, BaseAmount=Amount.
        var ethanId = Id("ethan@example.com");
        var onsite = await db.Trips.FirstOrDefaultAsync(t =>
            t.TravelerId == ethanId && t.Purpose == "Onsite integration work");
        if (onsite is not null && !await db.Expenses.AnyAsync(e => e.TripId == onsite.Id))
        {
            var enteredBy = Id("arranger@example.com");

            // Airfare: already reimbursed (lightweight status, independent of trip).
            var airfare = NewExpense(onsite.Id, ExpenseCategory.Airfare, new(2026, 6, 15), "United Airlines", 412.40m, enteredBy);
            airfare.Reimbursed = true;
            airfare.ReimbursedDate = new(2026, 6, 25);

            // Lodging folio split across categories (250 room + 39 in-room meals) that
            // reconciles to the 289.00 parent, so the rollup expands into categories.
            var lodging = NewExpense(onsite.Id, ExpenseCategory.Lodging, new(2026, 6, 16), "Marriott", 289.00m, enteredBy);
            lodging.Splits.Add(new ExpenseSplit { Category = ExpenseCategory.Lodging, Amount = 250.00m, BaseAmount = 250.00m, Description = "Room + tax" });
            lodging.Splits.Add(new ExpenseSplit { Category = ExpenseCategory.Meals, Amount = 39.00m, BaseAmount = 39.00m, Description = "Room service" });

            // Client dinner: Meals with attendees + business purpose substantiation.
            var dinner = NewExpense(onsite.Id, ExpenseCategory.Meals, new(2026, 6, 16), "Steakhouse 55", 96.75m, enteredBy);
            dinner.Attendees = "Ethan Brooks, Dana Wu (Acme, client)";
            dinner.BusinessPurpose = "Integration kickoff dinner";

            db.Expenses.AddRange(
                airfare,
                lodging,
                dinner,
                NewExpense(onsite.Id, ExpenseCategory.GroundTransport, new(2026, 6, 17), "Uber", 41.20m, enteredBy),
                NewExpense(onsite.Id, ExpenseCategory.Meals, new(2026, 6, 18), "Hotel minibar", 23.50m, enteredBy, isPersonal: true));
            await db.SaveChangesAsync();
        }

        // --- Mileage ---------------------------------------------------------
        // One round-trip drive with A→B→C waypoints on the same onsite trip, with a
        // commute deduction, so the rollup + frozen rate + waypoint route are visible.
        // The rate is FROZEN from the effective-dated table at the entry's date
        // (2026-06-17 → the 72.5¢ H1 2026 IRS rate), mirroring how the Create page works.
        if (onsite is not null && !await db.MileageEntries.AnyAsync(m => m.TripId == onsite.Id))
        {
            var enteredBy = Id("arranger@example.com");
            var mileageDate = new DateOnly(2026, 6, 17);
            var rates = await db.MileageRates.ToListAsync();
            var rate = MileageRateResolver.Resolve(
                rates, "US", VehicleType.Car, DistanceUnit.Miles, mileageDate);

            const decimal distance = 42.0m;   // one-way
            const decimal commute = 12.0m;    // normal commute, not reimbursable
            var billable = MileageMath.BillableDistance(distance, isRoundTrip: true, commute);

            db.MileageEntries.Add(new MileageEntry
            {
                TripId = onsite.Id,
                Date = mileageDate,
                Distance = distance,
                Unit = DistanceUnit.Miles,
                IsRoundTrip = true,
                CommuteDeduction = commute,
                Jurisdiction = "US",
                VehicleType = VehicleType.Car,
                RateId = rate?.Id,
                Rate = rate?.Rate ?? 0m,
                Amount = MileageMath.Amount(billable, rate?.Rate ?? 0m),
                Purpose = "Airport → client site → hotel",
                CreatedById = enteredBy,
                CreatedAt = DateTimeOffset.UtcNow,
                Waypoints =
                {
                    new MileageWaypoint { Sequence = 1, Label = "SFO Airport" },
                    new MileageWaypoint { Sequence = 2, Label = "Client site — Palo Alto" },
                    new MileageWaypoint { Sequence = 3, Label = "Hotel — Redwood City" },
                }
            });
            await db.SaveChangesAsync();
        }
    }

    private static Expense NewExpense(
        int tripId, ExpenseCategory category, DateOnly date, string vendor, decimal amount,
        string createdById, bool isPersonal = false)
        => new()
        {
            TripId = tripId,
            Category = category,
            Date = date,
            Vendor = vendor,
            Amount = amount,
            Currency = "USD",
            FxRate = 1m,
            FxRateDate = null,
            BaseAmount = amount,
            IsPersonal = isPersonal,
            CreatedById = createdById,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Trip NewTrip(
        string travelerId, string createdById, string purpose, TripStatus status,
        TripType type, DateOnly start, DateOnly end, params Destination[] legs)
        => new()
        {
            TravelerId = travelerId,
            CreatedById = createdById,
            Purpose = purpose,
            Status = status,
            Type = type,
            StartDate = start,
            EndDate = end,
            Destinations = legs.ToList(),
        };

    private static Destination Leg(
        string city, string state, string country, DateOnly arrive, DateOnly depart,
        TransportMode transport, string lodging, int sequence)
        => new()
        {
            City = city,
            State = state,
            Country = country,
            ArriveDate = arrive,
            DepartDate = depart,
            TransportMode = transport,
            LodgingName = lodging,
            Sequence = sequence,
        };
}
#endif
