using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Data;

// Production-safe evaluation/QA seeder. Unlike DevSeeder (which is #if DEBUG and
// absent from the Release artifact), EvalSeeder ships in the prod build so an eval
// data set can be stood up against a real environment and later removed cleanly by
// EvalTeardown.
//
// Two guardrails make it safe to compile into prod:
//   1. It NEVER runs on its own. Program.cs only calls it when the Eval:Seed flag
//      is set AND a non-empty Eval:BatchId is supplied out-of-band.
//   2. Every row it writes is quarantined from real data:
//        - AppUsers and Trips carry EvalBatchId = batchId (a filtered-indexed
//          column), so teardown finds them with an indexed delete.
//        - Reference data (departments, cost centers, project codes) is namespaced
//          with a batch-scoped prefix (see EvalNaming) that teardown removes.
//        - Users get eval-only emails on a throwaway domain, so they can't collide
//          with or shadow a real account.
//
// Mirrors DevSeeder's world (a user in every role, arranger delegations, an
// approver chain, trips across every status, expenses with splits/affidavits,
// mileage with waypoints, and a reject->approve approval trail) so evals exercise
// the same surfaces developers see locally.
//
// Idempotent: every entity is keyed on its eval-namespaced natural identifier and
// skipped if present, so re-running a batch is a no-op and a partially torn-down
// batch can be re-seeded.
public static class EvalSeeder
{
    private sealed record SeedUser(
        string Local, string DisplayName, string Role, string Department, string BaseLocation);

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

    // Local parts only; EvalSeeder namespaces them per batch/domain at runtime.
    private static readonly SeedUser[] Users =
    {
        new("manager",  "Morgan Bailey",  Roles.Manager,  "Engineering", "Seattle, WA"),
        new("finance",  "Fiona Chen",     Roles.Finance,  "Finance",     "New York, NY"),
        new("arranger", "Ava Delgado",    Roles.Arranger, "Operations",  "Chicago, IL"),
        new("arranger2","Alex Rivera",    Roles.Arranger, "Executive",   "Austin, TX"),
        new("evan",     "Evan Parker",    Roles.Employee, "Engineering", "Seattle, WA"),
        new("ella",     "Ella Nguyen",    Roles.Employee, "Sales",       "Denver, CO"),
        new("ethan",    "Ethan Brooks",   Roles.Employee, "Engineering", "Seattle, WA"),
        new("emma",     "Emma Sorensen",  Roles.Employee, "Operations",  "Chicago, IL"),
    };

    // (arranger local -> traveler locals they may act for)
    private static readonly (string Arranger, string[] Travelers)[] Delegations =
    {
        ("arranger",  new[] { "ethan", "emma" }),
        ("arranger2", new[] { "evan" }),
    };

    // (user local -> approver local). Two-level chain so the graph isn't flat.
    private static readonly (string User, string Approver)[] Approvers =
    {
        ("evan",    "manager"),
        ("ethan",   "manager"),
        ("ella",    "manager"),
        ("emma",    "manager"),
        ("manager", "arranger2"),
        ("finance", "arranger2"),
    };

    /// <param name="batchId">Non-empty tag written to every seeded user/trip and
    /// embedded in reference-data names; the handle EvalTeardown deletes by.</param>
    /// <param name="emailDomain">Throwaway domain for eval accounts.</param>
    /// <param name="password">Eval account password, supplied via config/secret —
    /// never a committed default.</param>
    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<AppUser> userManager,
        string batchId,
        string emailDomain,
        string password)
    {
        EvalNaming.ValidateBatchId(batchId);
        if (string.IsNullOrWhiteSpace(emailDomain))
            throw new ArgumentException("Eval email domain is required.", nameof(emailDomain));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(
                "Eval account password is required (set Eval:Password).", nameof(password));

        // --- Departments -----------------------------------------------------
        foreach (var name in Departments)
        {
            var deptName = EvalNaming.DeptName(name, batchId);
            if (!await db.Departments.AnyAsync(d => d.Name == deptName))
                db.Departments.Add(new Department { Name = deptName });
        }
        await db.SaveChangesAsync();

        var deptByName = new Dictionary<string, int>();
        foreach (var name in Departments)
        {
            var deptName = EvalNaming.DeptName(name, batchId);
            deptByName[name] = (await db.Departments.FirstAsync(d => d.Name == deptName)).Id;
        }

        // --- Cost centers / project codes ------------------------------------
        foreach (var (code, name) in CostCentersSeed)
        {
            var full = EvalNaming.Code(code, batchId);
            if (!await db.CostCenters.AnyAsync(c => c.Code == full))
                db.CostCenters.Add(new CostCenter { Code = full, Name = name, IsActive = true });
        }
        foreach (var (code, name) in ProjectCodesSeed)
        {
            var full = EvalNaming.Code(code, batchId);
            if (!await db.ProjectCodes.AnyAsync(p => p.Code == full))
                db.ProjectCodes.Add(new ProjectCode { Code = full, Name = name, IsActive = true });
        }
        await db.SaveChangesAsync();

        var engCc = (await db.CostCenters
            .FirstOrDefaultAsync(c => c.Code == EvalNaming.Code("ENG-100", batchId)))?.Id ?? 0;
        var opsCc = (await db.CostCenters
            .FirstOrDefaultAsync(c => c.Code == EvalNaming.Code("OPS-300", batchId)))?.Id ?? 0;
        var q1Proj = (await db.ProjectCodes
            .FirstOrDefaultAsync(p => p.Code == EvalNaming.Code("PRJ-2026-01", batchId)))?.Id ?? 0;

        // --- Users -----------------------------------------------------------
        // Keyed by local part; every account is stamped with EvalBatchId so
        // teardown can delete the batch without walking the object graph.
        var userByLocal = new Dictionary<string, AppUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in Users)
        {
            var email = EvalNaming.Email(seed.Local, batchId, emailDomain);
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = seed.DisplayName,
                    BaseLocation = seed.BaseLocation,
                    DepartmentId = deptByName.TryGetValue(seed.Department, out var did) ? did : null,
                    IsActive = true,
                    EvalBatchId = batchId,
                };
                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"EvalSeeder: failed to create {email}: " +
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(user, seed.Role);
            }
            userByLocal[seed.Local] = user;
        }

        string Id(string local) => userByLocal[local].Id;

        // --- Arranger delegations -------------------------------------------
        foreach (var (arrangerLocal, travelerLocals) in Delegations)
        {
            var arrangerId = Id(arrangerLocal);
            foreach (var travelerLocal in travelerLocals)
            {
                var travelerId = Id(travelerLocal);
                if (!await db.ArrangerAssignments
                        .AnyAsync(a => a.ArrangerId == arrangerId && a.TravelerId == travelerId))
                    db.ArrangerAssignments.Add(new ArrangerAssignment
                    {
                        ArrangerId = arrangerId,
                        TravelerId = travelerId,
                    });
            }
        }
        await db.SaveChangesAsync();

        // --- Approver relationships -----------------------------------------
        foreach (var (userLocal, approverLocal) in Approvers)
        {
            var user = userByLocal[userLocal];
            if (user.ApproverId is null)
            {
                user.ApproverId = Id(approverLocal);
                await userManager.UpdateAsync(user);
            }
        }

        // --- Traveler profile depth (non-sensitive only) --------------------
        var today = DateOnly.FromDateTime(DateTime.Today);
        var profiles = new (string Local, DateOnly Expiry, string Nationality, string Mobile)[]
        {
            ("ella",  today.AddMonths(-2), "United States", "+1 303 555 0101"), // expired
            ("evan",  today.AddMonths(3),  "United States", "+1 206 555 0102"), // expiring soon
            ("ethan", today.AddYears(6),   "United States", "+1 206 555 0103"), // fine
        };
        foreach (var (local, expiry, nationality, mobile) in profiles)
        {
            var u = userByLocal[local];
            if (u.PassportExpiry is null)
            {
                u.PassportExpiry = expiry;
                u.Nationality = nationality;
                u.MobileNumber = mobile;
                await userManager.UpdateAsync(u);
            }
        }

        // --- Trips -----------------------------------------------------------
        // Keyed on (TravelerId, Purpose). Every trip is stamped with EvalBatchId,
        // so a single delete-by-batch removes them and cascades their subtrees.
        var trips = new[]
        {
            NewTrip(batchId, Id("evan"), Id("arranger2"),
                "Cloud Summit 2026", TripStatus.Submitted, TripType.Conference,
                new(2026, 8, 3), new(2026, 8, 6),
                Leg("Las Vegas", "Nevada", "United States", new(2026, 8, 3), new(2026, 8, 6),
                    TransportMode.Flight, "Aria Resort", 1)),

            NewTrip(batchId, Id("ella"), Id("ella"),
                "Q3 client visit — Acme Corp", TripStatus.Submitted, TripType.ClientVisit,
                new(2026, 7, 28), new(2026, 7, 30),
                Leg("Austin", "Texas", "United States", new(2026, 7, 28), new(2026, 7, 30),
                    TransportMode.Flight, "Hyatt Regency", 1)),

            NewTrip(batchId, Id("ethan"), Id("arranger"),
                "Onsite integration work", TripStatus.Completed, TripType.Business,
                new(2026, 6, 15), new(2026, 6, 19),
                Leg("San Jose", "California", "United States", new(2026, 6, 15), new(2026, 6, 17),
                    TransportMode.Flight, "Marriott", 1),
                Leg("Portland", "Oregon", "United States", new(2026, 6, 17), new(2026, 6, 19),
                    TransportMode.Rail, "The Nines", 2)),

            NewTrip(batchId, Id("emma"), Id("emma"),
                "Operations training", TripStatus.Draft, TripType.Training,
                new(2026, 9, 1), new(2026, 9, 3),
                Leg("Toronto", "Ontario", "Canada", new(2026, 9, 1), new(2026, 9, 3),
                    TransportMode.Flight, "Fairmont Royal York", 1)),

            NewTrip(batchId, Id("manager"), Id("manager"),
                "Leadership offsite", TripStatus.Approved, TripType.Internal,
                new(2026, 8, 12), new(2026, 8, 14),
                Leg("Denver", "Colorado", "United States", new(2026, 8, 12), new(2026, 8, 14),
                    TransportMode.Flight, "The Brown Palace", 1)),

            NewTrip(batchId, Id("finance"), Id("finance"),
                "Year-end audit", TripStatus.Draft, TripType.Business,
                new(2026, 10, 5), new(2026, 10, 7),
                Leg("Boston", "Massachusetts", "United States", new(2026, 10, 5), new(2026, 10, 7),
                    TransportMode.Rail, "Omni Parker House", 1)),
        };

        foreach (var trip in trips)
        {
            if (!await db.Trips.AnyAsync(t =>
                    t.EvalBatchId == batchId && t.TravelerId == trip.TravelerId && t.Purpose == trip.Purpose))
                db.Trips.Add(trip);
        }
        await db.SaveChangesAsync();

        // --- Link cost center / project code onto this batch's trips ---------
        if (engCc != 0 && !await db.Trips.AnyAsync(t => t.EvalBatchId == batchId && t.CostCenterId != null))
        {
            foreach (var trip in await db.Trips.Where(t => t.EvalBatchId == batchId).ToListAsync())
            {
                trip.CostCenterId = engCc;
                if (q1Proj != 0) trip.ProjectCodeId = q1Proj;
            }
            foreach (var local in new[] { "ethan", "evan" })
                userByLocal[local].DefaultCostCenterId = engCc;
            if (opsCc != 0) userByLocal["emma"].DefaultCostCenterId = opsCc;
            await db.SaveChangesAsync();
        }

        // --- Expenses on Ethan's completed onsite ---------------------------
        async Task<Trip?> TripOf(string travelerLocal, string purpose)
        {
            var travelerId = Id(travelerLocal);
            return await db.Trips.FirstOrDefaultAsync(t =>
                t.EvalBatchId == batchId && t.TravelerId == travelerId && t.Purpose == purpose);
        }

        var onsite = await TripOf("ethan", "Onsite integration work");
        if (onsite is not null && !await db.Expenses.AnyAsync(e => e.TripId == onsite.Id))
        {
            var enteredBy = Id("arranger");

            var airfare = NewExpense(onsite.Id, ExpenseCategory.Airfare, new(2026, 6, 15), "United Airlines", 412.40m, enteredBy);
            airfare.Reimbursed = true;
            airfare.ReimbursedDate = new(2026, 6, 25);

            var lodging = NewExpense(onsite.Id, ExpenseCategory.Lodging, new(2026, 6, 16), "Marriott", 289.00m, enteredBy);
            lodging.Splits.Add(new ExpenseSplit { Category = ExpenseCategory.Lodging, Amount = 250.00m, BaseAmount = 250.00m, Description = "Room + tax" });
            lodging.Splits.Add(new ExpenseSplit { Category = ExpenseCategory.Meals, Amount = 39.00m, BaseAmount = 39.00m, Description = "Room service" });

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

        // --- Mileage on the same onsite -------------------------------------
        if (onsite is not null && !await db.MileageEntries.AnyAsync(m => m.TripId == onsite.Id))
        {
            var enteredBy = Id("arranger");
            var mileageDate = new DateOnly(2026, 6, 17);
            var rates = await db.MileageRates.ToListAsync();
            var rate = MileageRateResolver.Resolve(rates, "US", VehicleType.Car, DistanceUnit.Miles, mileageDate);

            const decimal distance = 42.0m;
            const decimal commute = 12.0m;
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

        // --- Expenses & mileage across the other trips ----------------------
        async Task SeedExpensesAsync(Trip? trip, params Expense[] lines)
        {
            if (trip is null || await db.Expenses.AnyAsync(e => e.TripId == trip.Id))
                return;
            db.Expenses.AddRange(lines);
            await db.SaveChangesAsync();
        }

        async Task SeedMileageAsync(
            Trip? trip, string enteredBy, DateOnly date, decimal distance,
            bool roundTrip, decimal commute, string purpose, params string[] waypoints)
        {
            if (trip is null || await db.MileageEntries.AnyAsync(m => m.TripId == trip.Id))
                return;

            var rateRows = await db.MileageRates.ToListAsync();
            var resolved = MileageRateResolver.Resolve(rateRows, "US", VehicleType.Car, DistanceUnit.Miles, date);
            var billable = MileageMath.BillableDistance(distance, roundTrip, commute);

            var entry = new MileageEntry
            {
                TripId = trip.Id,
                Date = date,
                Distance = distance,
                Unit = DistanceUnit.Miles,
                IsRoundTrip = roundTrip,
                CommuteDeduction = commute,
                Jurisdiction = "US",
                VehicleType = VehicleType.Car,
                RateId = resolved?.Id,
                Rate = resolved?.Rate ?? 0m,
                Amount = MileageMath.Amount(billable, resolved?.Rate ?? 0m),
                Purpose = purpose,
                CreatedById = enteredBy,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            for (var i = 0; i < waypoints.Length; i++)
                entry.Waypoints.Add(new MileageWaypoint { Sequence = i + 1, Label = waypoints[i] });

            db.MileageEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var summit = await TripOf("evan", "Cloud Summit 2026");
        var summitEnteredBy = Id("arranger2");
        var summitKiosk = NewExpense(summit?.Id ?? 0, ExpenseCategory.Meals, new(2026, 8, 5), "Terminal 3 kiosk", 31.00m, summitEnteredBy);
        summitKiosk.MissingReceiptAffidavit = "Grab-and-go lunch; kiosk issued no printed receipt.";
        await SeedExpensesAsync(summit,
            NewExpense(summit?.Id ?? 0, ExpenseCategory.Airfare, new(2026, 8, 3), "Southwest Airlines", 356.80m, summitEnteredBy),
            NewExpense(summit?.Id ?? 0, ExpenseCategory.Lodging, new(2026, 8, 3), "Aria Resort", 540.00m, summitEnteredBy),
            NewExpense(summit?.Id ?? 0, ExpenseCategory.Conference, new(2026, 8, 3), "Cloud Summit registration", 799.00m, summitEnteredBy),
            NewExpense(summit?.Id ?? 0, ExpenseCategory.GroundTransport, new(2026, 8, 4), "Lyft", 28.90m, summitEnteredBy),
            summitKiosk);

        var clientVisit = await TripOf("ella", "Q3 client visit — Acme Corp");
        var ellaId = Id("ella");
        var clientDinner = NewExpense(clientVisit?.Id ?? 0, ExpenseCategory.Meals, new(2026, 7, 29), "Uchi", 118.40m, ellaId);
        clientDinner.Attendees = "Ella Nguyen, Priya Shah (Acme, client)";
        clientDinner.BusinessPurpose = "Q3 account review dinner";
        var ellaParking = NewExpense(clientVisit?.Id ?? 0, ExpenseCategory.Parking, new(2026, 7, 28), "Airport economy lot", 27.00m, ellaId);
        ellaParking.MissingReceiptAffidavit = "Unattended lot; no receipt dispensed at exit.";
        await SeedExpensesAsync(clientVisit,
            NewExpense(clientVisit?.Id ?? 0, ExpenseCategory.Airfare, new(2026, 7, 28), "American Airlines", 289.50m, ellaId),
            NewExpense(clientVisit?.Id ?? 0, ExpenseCategory.Lodging, new(2026, 7, 28), "Hyatt Regency", 318.00m, ellaId),
            clientDinner,
            ellaParking,
            NewExpense(clientVisit?.Id ?? 0, ExpenseCategory.Meals, new(2026, 7, 29), "In-room movie", 17.99m, ellaId, isPersonal: true));

        var offsite = await TripOf("manager", "Leadership offsite");
        var managerId = Id("manager");
        var offsiteAir = NewExpense(offsite?.Id ?? 0, ExpenseCategory.Airfare, new(2026, 8, 12), "United Airlines", 214.60m, managerId);
        offsiteAir.Reimbursed = true;
        offsiteAir.ReimbursedDate = new(2026, 8, 20);
        var offsiteDinner = NewExpense(offsite?.Id ?? 0, ExpenseCategory.Meals, new(2026, 8, 13), "The Palm", 176.20m, managerId);
        offsiteDinner.Attendees = "Morgan Bailey, Evan Parker, Ethan Brooks";
        offsiteDinner.BusinessPurpose = "Leadership team working dinner";
        await SeedExpensesAsync(offsite,
            offsiteAir,
            NewExpense(offsite?.Id ?? 0, ExpenseCategory.Lodging, new(2026, 8, 12), "The Brown Palace", 402.00m, managerId),
            offsiteDinner);

        // --- Approval history: reject -> approve on the offsite -------------
        if (offsite is not null && !await db.Approvals.AnyAsync(a => a.TripId == offsite.Id))
        {
            var alexId = Id("arranger2");
            db.Approvals.AddRange(
                new Approval
                {
                    TripId = offsite.Id,
                    ApproverId = alexId,
                    Decision = ApprovalDecision.Rejected,
                    Comment = "Please add the working-dinner business purpose before I sign off.",
                    DecidedAt = DateTimeOffset.UtcNow.AddDays(-3),
                },
                new Approval
                {
                    TripId = offsite.Id,
                    ApproverId = alexId,
                    Decision = ApprovalDecision.Approved,
                    Comment = "Thanks — approved.",
                    DecidedAt = DateTimeOffset.UtcNow.AddDays(-2),
                });
            await db.SaveChangesAsync();
        }

        await SeedMileageAsync(clientVisit, ellaId, new DateOnly(2026, 7, 28), 34.0m,
            roundTrip: true, commute: 0m, "Home → Austin-Bergstrom airport",
            "Home", "AUS Airport");
        await SeedMileageAsync(offsite, managerId, new DateOnly(2026, 8, 12), 58.0m,
            roundTrip: true, commute: 15.0m, "Home → offsite venue",
            "Home", "The Brown Palace, Denver");
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
        string batchId, string travelerId, string createdById, string purpose, TripStatus status,
        TripType type, DateOnly start, DateOnly end, params Destination[] legs)
        => new()
        {
            EvalBatchId = batchId,
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
