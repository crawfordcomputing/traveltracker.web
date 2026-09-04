using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Trips;

public class DetailsModel : TripPageModel
{
    private readonly IEmailSender _email;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(AppDbContext db, TeamAccess team, IEmailSender email, ILogger<DetailsModel> logger)
        : base(db, team)
    {
        _email = email;
        _logger = logger;
    }

    public Trip Trip { get; private set; } = default!;
    public IReadOnlyList<TripStatus> NextStates { get; private set; } = Array.Empty<TripStatus>();
    // The lifecycle moves the traveler (owner/delegate) may drive from here.
    public IReadOnlyList<TripTransition> TravelerTransitions { get; private set; } = Array.Empty<TripTransition>();
    // True when the current user is the trip traveler's assigned approver (or an
    // Admin acting as one) and so may approve/reject a Submitted trip.
    public bool CanApprove { get; private set; }
    // Approval decision history, oldest first.
    public IReadOnlyList<Approval> ApprovalHistory { get; private set; } = Array.Empty<Approval>();
    public IReadOnlyList<Trip> Conflicts { get; private set; } = Array.Empty<Trip>();

    // Whether the itinerary (legs) may be added/edited/removed in the trip's current
    // status. Drives both the UI (hide the edit controls) and the handler guards.
    public bool CanEditItinerary { get; private set; }

    private const string ItineraryLockedMessage =
        "This trip's itinerary is locked in its current status. Use \"Revise itinerary\" "
        + "to send an approved trip back to draft before changing its legs.";

    public IReadOnlyList<Expense> Expenses { get; private set; } = Array.Empty<Expense>();
    public ExpenseRollupResult Rollup { get; private set; } = ExpenseRollupResult.Empty;

    // Configurable per-category guideline caps for the over-guideline badge. Loaded
    // once per request and handed to ExpensePolicyCheck in the view. See ADR-0003.
    public IReadOnlyDictionary<TravelTracker.Web.Data.Entities.ExpenseCategory, decimal> Caps { get; private set; }
        = ExpensePolicyCheck.NoCaps;

    public IReadOnlyList<MileageEntry> MileageEntries { get; private set; } = Array.Empty<MileageEntry>();
    public MileageRollupResult MileageTotals { get; private set; } = MileageRollupResult.Empty;

    public SelectList Countries { get; private set; } = default!;
    public SelectList States { get; private set; } = default!;

    [BindProperty] public LegInput NewLeg { get; set; } = new();

    public class LegInput
    {
        [Required, StringLength(120)] public string City { get; set; } = string.Empty;
        [StringLength(120)] public string? State { get; set; }
        [Required, StringLength(120)] public string Country { get; set; } = "United States";

        [Display(Name = "Arrive"), DataType(DataType.Date)]
        public DateOnly ArriveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Display(Name = "Depart"), DataType(DataType.Date)]
        public DateOnly DepartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Display(Name = "Transport")] public TransportMode TransportMode { get; set; } = TransportMode.Unspecified;
        [Display(Name = "Lodging"), StringLength(160)] public string? LodgingName { get; set; }
        [Display(Name = "Confirmation #"), StringLength(80)] public string? ConfirmationNumber { get; set; }
        [StringLength(1000)] public string? Notes { get; set; }
    }

    private async Task<bool> LoadAsync(int id)
    {
        Func<IQueryable<Trip>, IQueryable<Trip>> include =
            q => q.Include(t => t.Traveler).ThenInclude(u => u!.Approver)
                  .Include(t => t.Traveler).ThenInclude(u => u!.Department)
                        .ThenInclude(d => d!.DefaultApprover)
                  .Include(t => t.Destinations)
                  .Include(t => t.CostCenter)
                  .Include(t => t.ProjectCode);

        var trip = await LoadAuthorizedTripAsync(id, include);

        // The traveler's assigned approver may not sit in their management chain
        // (approver != manager), so the standard access check can miss them. Re-load
        // and allow if the current user is this trip's approver — they must be able
        // to open it to approve/reject.
        if (trip is null)
        {
            var candidate = await include(Db.Trips).FirstOrDefaultAsync(t => t.Id == id);
            var approverId = candidate is null ? null : EffectiveApproverId(candidate);
            if (candidate is not null && approverId is not null && approverId == CurrentUserId)
                trip = candidate;
        }

        if (trip is null) return false;
        Trip = trip;
        NextStates = TripStatusRules.NextStates(trip.Status);
        TravelerTransitions = TripStatusRules.TransitionsFor(trip.Status, TripActor.Traveler);
        CanEditItinerary = TripStatusRules.ItineraryEditable(trip.Status);
        CanApprove = IsApproverOf(trip);

        ApprovalHistory = await Db.Approvals
            .Include(a => a.Approver)
            .Where(a => a.TripId == trip.Id)
            .OrderBy(a => a.DecidedAt)
            .ThenBy(a => a.Id)
            .ToListAsync();

        var otherTrips = await Db.Trips
            .Where(t => t.TravelerId == trip.TravelerId && t.Id != trip.Id)
            .ToListAsync();
        Conflicts = TripOverlap
            .FindConflicts(otherTrips, trip.StartDate, trip.EndDate)
            .OrderBy(t => t.StartDate)
            .ToList();

        Expenses = await Db.Expenses
            .Include(e => e.Splits)
            .Where(e => e.TripId == trip.Id)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .ToListAsync();
        Rollup = ExpenseRollup.Summarize(Expenses);
        Caps = ExpensePolicyCheck.CapsFrom(await Db.ExpensePolicies.ToListAsync());

        MileageEntries = await Db.MileageEntries
            .Include(m => m.Waypoints)
            .Where(m => m.TripId == trip.Id)
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.Id)
            .ToListAsync();
        MileageTotals = MileageRollup.Summarize(MileageEntries);

        await LoadLookupsAsync();
        return true;
    }

    private async Task LoadLookupsAsync()
    {
        Countries = new SelectList(
            await Db.Countries.OrderBy(c => c.Name).ToListAsync(),
            "Name", "Name", NewLeg.Country);
        States = new SelectList(
            await Db.UsStates.OrderBy(s => s.Name).ToListAsync(),
            "Name", "Name", NewLeg.State);
    }

    public async Task<IActionResult> OnGetAsync(int id)
        => await LoadAsync(id) ? Page() : NotFound();

    // Itinerary calendar download. Authorized through TripAccess (same as the page),
    // so a trip outside the caller's scope 404s rather than leaking. One all-day
    // VEVENT per leg; re-import updates via the stable per-leg UID.
    public async Task<IActionResult> OnGetIcsAsync(int id)
    {
        var trip = await LoadAuthorizedTripAsync(id, q => q.Include(t => t.Destinations));
        if (trip is null) return NotFound();

        var bytes = IcsBuilder.ToBytes(trip, trip.Destinations, DateTimeOffset.UtcNow);
        return File(bytes, "text/calendar", $"trip-{trip.Id}.ics");
    }

    public async Task<IActionResult> OnPostAddLegAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();

        if (!CanEditItinerary)
        {
            TempData["TripError"] = ItineraryLockedMessage;
            return RedirectToPage("Details", new { id });
        }

        var legError = TripDateRules.ValidateLeg(NewLeg.ArriveDate, NewLeg.DepartDate);
        if (legError is not null)
            ModelState.AddModelError("NewLeg.DepartDate", legError);

        if (!ModelState.IsValid)
            return Page();

        var nextSeq = Trip.Destinations.Count == 0 ? 1 : Trip.Destinations.Max(d => d.Sequence) + 1;
        Db.Destinations.Add(new Destination
        {
            TripId = Trip.Id,
            City = NewLeg.City.Trim(),
            State = string.IsNullOrWhiteSpace(NewLeg.State) ? null : NewLeg.State.Trim(),
            Country = NewLeg.Country.Trim(),
            ArriveDate = NewLeg.ArriveDate,
            DepartDate = NewLeg.DepartDate,
            TransportMode = NewLeg.TransportMode,
            LodgingName = string.IsNullOrWhiteSpace(NewLeg.LodgingName) ? null : NewLeg.LodgingName.Trim(),
            ConfirmationNumber = string.IsNullOrWhiteSpace(NewLeg.ConfirmationNumber) ? null : NewLeg.ConfirmationNumber.Trim(),
            Notes = string.IsNullOrWhiteSpace(NewLeg.Notes) ? null : NewLeg.Notes.Trim(),
            Sequence = nextSeq
        });
        await Db.SaveChangesAsync();
        await ReconcileWindowAsync(Trip.Id);
        return RedirectToPage("Details", new { id });
    }

    public async Task<IActionResult> OnPostRemoveLegAsync(int id, int legId)
    {
        if (!await LoadAsync(id)) return NotFound();

        if (!CanEditItinerary)
        {
            TempData["TripError"] = ItineraryLockedMessage;
            return RedirectToPage("Details", new { id });
        }

        var leg = await Db.Destinations.FirstOrDefaultAsync(d => d.Id == legId && d.TripId == id);
        if (leg is not null)
        {
            Db.Destinations.Remove(leg);
            await Db.SaveChangesAsync();
            await ReconcileWindowAsync(id);
        }
        return RedirectToPage("Details", new { id });
    }

    // Copy the traveler's most recent OTHER trip's expense lines into this trip.
    // Dates rebase to this trip's start; receipts and reimbursement state don't carry
    // (see ExpenseClone). Advisory convenience for recurring routes.
    public async Task<IActionResult> OnPostCloneExpensesAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();

        var source = await Db.Trips
            .Where(t => t.TravelerId == Trip.TravelerId && t.Id != Trip.Id)
            .Where(t => Db.Expenses.Any(e => e.TripId == t.Id))
            .OrderByDescending(t => t.StartDate)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync();

        if (source is null)
        {
            TempData["ExpenseInfo"] = "No earlier trip with expenses to clone from.";
            return RedirectToPage("Details", new { id });
        }

        var sourceLines = await Db.Expenses
            .Include(e => e.Splits)
            .Where(e => e.TripId == source.Id)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        foreach (var line in sourceLines)
            Db.Expenses.Add(ExpenseClone.From(line, Trip.Id, CurrentUserId, Trip.StartDate, now));

        await Db.SaveChangesAsync();
        TempData["ExpenseInfo"] =
            $"Cloned {sourceLines.Count} expense line(s) from “{source.Purpose}”. Review dates and receipts.";
        return RedirectToPage("Details", new { id });
    }

    // Flip an expense line's lightweight reimbursement marker (independent of trip
    // status; the full approval flow is M6). Stamps/clears the date to today.
    public async Task<IActionResult> OnPostToggleReimbursedAsync(int id, int expenseId)
    {
        if (!await LoadAsync(id)) return NotFound();

        var expense = await Db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId && e.TripId == id);
        if (expense is not null)
        {
            expense.Reimbursed = !expense.Reimbursed;
            expense.ReimbursedDate = expense.Reimbursed ? DateOnly.FromDateTime(DateTime.Today) : null;
            await Db.SaveChangesAsync();
        }
        return RedirectToPage("Details", new { id });
    }

    // Traveler-driven lifecycle moves (submit/withdraw/revise/complete/cancel/reopen).
    // Approver decisions (Approved/Rejected) go through OnPostApprove/OnPostReject so
    // they can be authorized and recorded in the approval history.
    public async Task<IActionResult> OnPostStatusAsync(int id, TripStatus target, string? reason = null)
    {
        if (!await LoadAsync(id)) return NotFound();

        // Submit needs an approver check + notification; decisions need approver
        // auth + a history row. Both have dedicated handlers.
        if (target is TripStatus.Submitted or TripStatus.Approved or TripStatus.Rejected)
        {
            TempData["TripError"] = "Use the approval actions for that change.";
            return RedirectToPage("Details", new { id });
        }

        if (!TripStatusRules.CanTransition(Trip.Status, target, TripActor.Traveler))
        {
            TempData["TripError"] = "That status change is not allowed.";
            return RedirectToPage("Details", new { id });
        }

        if (target == TripStatus.Cancelled && string.IsNullOrWhiteSpace(reason))
        {
            TempData["TripError"] = "A reason is required to cancel a trip.";
            return RedirectToPage("Details", new { id });
        }

        // Reopening an Approved trip (Approved -> Draft "Revise itinerary") invalidates
        // the approver's sign-off. Record it in the same history log the approve/reject
        // decisions use, so the trail shows the approval was deliberately reopened.
        var wasApproved = TripStatusRules.ItineraryEditable(Trip.Status) == false
                          && target == TripStatus.Draft
                          && Trip.Status is TripStatus.Approved or TripStatus.Planned;

        Trip.Status = target;
        Trip.CancellationReason = target == TripStatus.Cancelled ? reason!.Trim() : null;

        if (wasApproved)
        {
            Db.Approvals.Add(new Approval
            {
                TripId = Trip.Id,
                ApproverId = CurrentUserId ?? string.Empty,
                Decision = ApprovalDecision.Reopened,
                Comment = "Reopened for itinerary revision.",
                DecidedAt = DateTimeOffset.UtcNow
            });
        }

        await Db.SaveChangesAsync();
        return RedirectToPage("Details", new { id });
    }

    // Traveler submits a Draft/Rejected trip to their approver.
    public async Task<IActionResult> OnPostSubmitAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();

        if (!TripStatusRules.CanTransition(Trip.Status, TripStatus.Submitted, TripActor.Traveler))
        {
            TempData["TripError"] = "This trip can't be submitted from its current status.";
            return RedirectToPage("Details", new { id });
        }

        var approver = EffectiveApprover(Trip);
        if (approver is null)
        {
            TempData["TripError"] =
                "No approver is assigned to this traveler, and their department has no default "
                + "approver. Ask an admin to set one before submitting.";
            return RedirectToPage("Details", new { id });
        }

        Trip.Status = TripStatus.Submitted;
        await Db.SaveChangesAsync();

        // Absolute link to this trip's Details page, where the approver acts
        // (same Url.Page + Request.Scheme pattern as the password-reset email).
        var approvalUrl = Url.Page("/Trips/Details", pageHandler: null,
            values: new { id }, protocol: Request.Scheme);

        await TrySendAsync(approver.Email,
            $"Trip awaiting your approval: {Trip.Purpose}",
            $"<p>{H(Trip.Traveler?.DisplayName)} submitted a trip for your approval.</p>" +
            $"<p><strong>{H(Trip.Purpose)}</strong><br>" +
            $"{Trip.StartDate:MMM d} – {Trip.EndDate:MMM d, yyyy}</p>" +
            $"<p><a href=\"{H(approvalUrl)}\">Review and approve or reject this trip</a></p>");

        TempData["ExpenseInfo"] = $"Submitted to {approver.DisplayName} for approval.";
        return RedirectToPage("Details", new { id });
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, string? comment = null)
        => await DecideAsync(id, ApprovalDecision.Approved, comment);

    public async Task<IActionResult> OnPostRejectAsync(int id, string? comment = null)
        => await DecideAsync(id, ApprovalDecision.Rejected, comment);

    // Shared approve/reject path: authorize the approver, enforce the transition,
    // record an immutable Approval row, then notify the traveler.
    private async Task<IActionResult> DecideAsync(int id, ApprovalDecision decision, string? comment)
    {
        if (!await LoadAsync(id)) return NotFound();

        if (!CanApprove)
        {
            TempData["TripError"] = "Only this traveler's approver can decide on this trip.";
            return RedirectToPage("Details", new { id });
        }

        var target = decision == ApprovalDecision.Approved ? TripStatus.Approved : TripStatus.Rejected;
        if (!TripStatusRules.CanTransition(Trip.Status, target, TripActor.Approver))
        {
            TempData["TripError"] = "This trip is not awaiting a decision.";
            return RedirectToPage("Details", new { id });
        }

        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (decision == ApprovalDecision.Rejected && comment is null)
        {
            TempData["TripError"] = "A comment is required when rejecting so the traveler knows what to change.";
            return RedirectToPage("Details", new { id });
        }

        Db.Approvals.Add(new Approval
        {
            TripId = Trip.Id,
            ApproverId = CurrentUserId!,
            Decision = decision,
            Comment = comment,
            DecidedAt = DateTimeOffset.UtcNow,
        });
        Trip.Status = target;
        await Db.SaveChangesAsync();

        var verb = decision == ApprovalDecision.Approved ? "approved" : "rejected";
        await TrySendAsync(Trip.Traveler?.Email,
            $"Your trip was {verb}: {Trip.Purpose}",
            $"<p>Your trip <strong>{H(Trip.Purpose)}</strong> ({Trip.StartDate:MMM d} – {Trip.EndDate:MMM d, yyyy}) " +
            $"was {verb} by {H(EffectiveApprover(Trip)?.DisplayName ?? "your approver")}.</p>" +
            (comment is null ? "" : $"<p>Comment: {H(comment)}</p>"));

        TempData["ExpenseInfo"] = $"Trip {verb}.";
        return RedirectToPage("Details", new { id });
    }

    // The current user is the trip traveler's assigned approver, or an Admin acting
    // as one. Admin override keeps a stuck queue unblockable if an approver leaves.
    private bool IsApproverOf(Trip trip)
    {
        var approverId = EffectiveApproverId(trip);
        return (approverId is not null && approverId == CurrentUserId) || User.IsInRole(Roles.Admin);
    }

    // Effective approver of a trip's traveler: their own ApproverId, else their
    // department default (see Domain/ApproverResolution). Requires Traveler,
    // Traveler.Approver, and Traveler.Department.DefaultApprover to be loaded.
    private static string? EffectiveApproverId(Trip trip)
    {
        var t = trip.Traveler;
        return t is null
            ? null
            : ApproverResolution.EffectiveApproverId(t.Id, t.ApproverId, t.Department?.DefaultApproverId);
    }

    private static AppUser? EffectiveApprover(Trip trip)
    {
        var t = trip.Traveler;
        if (t is null) return null;
        var id = ApproverResolution.EffectiveApproverId(t.Id, t.ApproverId, t.Department?.DefaultApproverId);
        if (id is null) return null;
        return t.ApproverId == id ? t.Approver : t.Department?.DefaultApprover;
    }

    // HTML-encode user-supplied text (purpose, display names, comments) before it is
    // interpolated into an email body, so nobody can inject markup or links into
    // notifications. Subjects are plain text and need no encoding.
    private static string H(string? text) => HtmlEncoder.Default.Encode(text ?? string.Empty);

    // Email is best-effort: an approval is already persisted, so a mail outage must
    // not fail the request. Log and move on.
    private async Task TrySendAsync(string? recipient, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return;
        try
        {
            await _email.SendAsync(recipient, subject, htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Approval notification email to {Recipient} failed.", recipient);
        }
    }

    // Expands the trip window to contain all its legs (buffer days preserved).
    private async Task ReconcileWindowAsync(int tripId)
    {
        var trip = await Db.Trips.Include(t => t.Destinations)
            .FirstOrDefaultAsync(t => t.Id == tripId);
        if (trip is null || trip.Destinations.Count == 0) return;

        var (start, end) = TripDateRules.ReconcileWindow(trip.StartDate, trip.EndDate, trip.Destinations);
        if (start != trip.StartDate || end != trip.EndDate)
        {
            trip.StartDate = start;
            trip.EndDate = end;
            await Db.SaveChangesAsync();
        }
    }
}
