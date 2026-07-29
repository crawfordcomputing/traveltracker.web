using TravelTracker.Web.Auth;
using TravelTracker.Web.Data;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    // Everything under /Admin requires the Admin role.
    options.Conventions.AuthorizeFolder("/Admin", "RequireAdmin");
    // Trips require an authenticated user (any role).
    options.Conventions.AuthorizeFolder("/Trips");
    // Expenses ride the parent trip's scope but still require being signed in.
    options.Conventions.AuthorizeFolder("/Expenses");
    // Mileage rides the parent trip's scope but still requires being signed in.
    options.Conventions.AuthorizeFolder("/Mileage");
    // Reports are a finance/management surface; ReportAccess scopes the data within.
    options.Conventions.AuthorizeFolder("/Reports", "RequireReports");
    // Account self-management (e.g. change password) requires being signed in.
    options.Conventions.AuthorizeFolder("/Account/Manage");
    // Reference-data management (M5). The two pages differ in who may manage them,
    // so they're authorized per-page rather than as a single folder.
    options.Conventions.AuthorizePage("/Reference/CostCenters/Index", "RequireCostCenters");
    options.Conventions.AuthorizePage("/Reference/ProjectCodes/Index", "RequireProjectCodes");
});
builder.Services.AddAppDatabase(builder.Configuration);
builder.Services.AddAppIdentity(builder.Configuration);
builder.Services.AddAppEmail(builder.Configuration);
// Receipt file storage: local disk by default, Azure Blob via Storage:Provider.
builder.Services.AddReceiptStorage(builder.Configuration);
builder.Services.AddScoped<TeamAccess>();
// Approver queue (M6): powers the nav badge + the Trips/Approvals page.
builder.Services.AddScoped<ApprovalInbox>();
// JIT Entra group->role sync on external login (no-op unless Entra + mappings on).
builder.Services.AddScoped<EntraRoleSynchronizer>();
// Field-level encryption for sensitive profile identifiers (passport / KTN).
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ISensitiveFieldProtector, SensitiveFieldProtector>();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// When enabled, corral Admins without TOTP enrolled to the setup page. Registered only
// when the toggle is on so the per-request lookup is never paid otherwise.
if (app.Configuration.GetValue<bool>("Auth:Mfa:RequireForAdmins"))
{
    app.UseMiddleware<MfaEnforcementMiddleware>();
}

app.MapRazorPages();
app.MapHealthChecks("/healthz");

// Apply migrations + seed on startup so the DB self-initializes on first run/deploy.
await DbInitializer.InitializeAsync(app.Services, app.Configuration);

// Evaluation/QA data (production-safe). Unlike the DevSeeder block below, EvalSeeder
// and EvalTeardown are compiled into every build. They never act on their own: this
// runs only when Eval:Seed or Eval:Teardown is explicitly set AND a non-empty
// Eval:BatchId is supplied out-of-band. Teardown wins if both flags are set. Every
// row EvalSeeder writes is tagged (EvalBatchId) or batch-namespaced, so EvalTeardown
// removes exactly that batch and nothing else. See Data/EvalSeeder + Data/EvalTeardown.
{
    var evalConfig = app.Configuration.GetSection("Eval");
    var doSeed = evalConfig.GetValue<bool>("Seed");
    var doTeardown = evalConfig.GetValue<bool>("Teardown");
    if (doSeed || doTeardown)
    {
        var batchId = evalConfig["BatchId"];
        if (string.IsNullOrWhiteSpace(batchId))
            throw new InvalidOperationException(
                "Eval:Seed/Eval:Teardown is set but Eval:BatchId is empty. Refusing to run " +
                "an eval seed or teardown without an explicit batch id.");

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("EvalData");

        if (doTeardown)
        {
            var result = await EvalTeardown.RemoveAsync(db, batchId);
            logger.LogWarning("Eval teardown for batch {BatchId} removed {Result}.", batchId, result);
        }
        else
        {
            var domain = evalConfig["EmailDomain"] ?? "eval.traveltracker.test";
            await EvalSeeder.SeedAsync(
                db,
                sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TravelTracker.Web.Data.Entities.AppUser>>(),
                batchId,
                domain,
                evalConfig["Password"]!);
            logger.LogWarning("Eval seed for batch {BatchId} applied (domain {Domain}).", batchId, domain);
        }
    }
}

#if DEBUG
// Development-only sample data (departments, users in every role, arranger
// delegations, trips). Compiled only in Debug builds, so DevSeeder is absent from
// the Release/production artifact entirely. Idempotent; never runs outside
// Development, and gated behind Seed:DevData so integration tests (which boot as
// Development) can opt out via UseSetting("Seed:DevData", "false").
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Seed:DevData"))
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    await DevSeeder.SeedAsync(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TravelTracker.Web.Data.Entities.AppUser>>());
}
#endif

app.Run();

// Exposed for integration testing (WebApplicationFactory).
public partial class Program { }
