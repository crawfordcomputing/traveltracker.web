using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services.Email;

namespace TravelTracker.Web.Pages.Admin.Invites;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailTemplateService _email;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager,
        IEmailTemplateService email, ILogger<IndexModel> logger)
    {
        _db = db;
        _userManager = userManager;
        _email = email;
        _logger = logger;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public SelectList Departments { get; private set; } = default!;
    public SelectList RoleOptions { get; private set; } = default!;

    public record Row(int Id, string Email, string Role, string? Department,
        DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, bool Expired)
    {
        // Only open (unaccepted, unexpired) invites can be re-sent.
        public bool CanResend => AcceptedAt is null && !Expired;
    }
    public List<Row> Invites { get; private set; } = new();

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Role { get; set; } = Roles.Employee;
        [Display(Name = "Department")] public int? DepartmentId { get; set; }
        [Range(1, 60), Display(Name = "Valid for (days)")] public int ExpiresInDays { get; set; } = 7;
    }

    private async Task LoadAsync()
    {
        Departments = await _db.DepartmentSelectListAsync();
        RoleOptions = ReferenceLists.RoleSelectList();

        var now = DateTimeOffset.UtcNow;
        Invites = await _db.Invitations
            .Include(i => i.Department)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new Row(i.Id, i.Email, i.Role,
                i.Department != null ? i.Department.Name : null,
                i.ExpiresAt, i.AcceptedAt, i.AcceptedAt == null && i.ExpiresAt <= now))
            .ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var email = Input.Email.Trim();

        if (!Roles.All.Contains(Input.Role))
            ModelState.AddModelError("Input.Role", "Unknown role.");
        if (await _userManager.FindByEmailAsync(email) is not null)
            ModelState.AddModelError("Input.Email", "A user with that email already exists.");
        if (await _db.Invitations.AnyAsync(i => i.Email == email && i.AcceptedAt == null))
            ModelState.AddModelError("Input.Email", "An open invite for that email already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var token = InviteTokens.NewToken();
        var invite = new Invitation
        {
            Email = email,
            Role = Input.Role,
            DepartmentId = Input.DepartmentId,
            TokenHash = InviteTokens.Hash(token),
            InvitedByUserId = _userManager.GetUserId(User) ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(Input.ExpiresInDays)
        };
        _db.Invitations.Add(invite);
        await _db.SaveChangesAsync();

        await SendAndSurfaceAsync(invite, token);
        return RedirectToPage();
    }

    // Re-sends an open invite. Only the token's hash is stored, so the original link
    // can't be rebuilt: a fresh token is minted and replaces the hash (the old link
    // stops working), while ExpiresAt is left untouched. See ADR-0005.
    public async Task<IActionResult> OnPostResendAsync(int id)
    {
        var invite = await _db.Invitations.FindAsync(id);
        if (invite is null || !invite.IsRedeemable(DateTimeOffset.UtcNow))
            return RedirectToPage();

        var token = InviteTokens.NewToken();
        invite.TokenHash = InviteTokens.Hash(token);
        await _db.SaveChangesAsync();

        await SendAndSurfaceAsync(invite, token);
        return RedirectToPage();
    }

    // Emails the invite link (best-effort) and always surfaces it on screen too.
    // The raw token exists only here — it's never recoverable after this render, so
    // the banner stays as the fallback when delivery fails or the admin would rather
    // hand the link over another way.
    private async Task SendAndSurfaceAsync(Invitation invite, string token)
    {
        var link = Url.Page("/Account/Register", pageHandler: null,
            values: new { token }, protocol: Request.Scheme);

        var inviter = await _userManager.GetUserAsync(User);
        var sent = await TrySendAsync(invite.Email, new Dictionary<string, string?>
        {
            ["InviteLink"] = link,
            ["Invite.Role"] = invite.Role,
            ["Invite.ExpiresAt"] = EmailTemplateTokens.InviteExpiry(invite.ExpiresAt),
            ["InvitedBy.Name"] = string.IsNullOrWhiteSpace(inviter?.DisplayName) ? "An administrator" : inviter.DisplayName,
        });

        TempData["InviteEmail"] = invite.Email;
        TempData["InviteLink"] = link;
        TempData["InviteEmailed"] = sent;
    }

    // Invite email is best-effort like the trip notifications: the invite row is
    // already saved and the link is on screen, so a mail outage must not fail the request.
    private async Task<bool> TrySendAsync(string recipient, IReadOnlyDictionary<string, string?> tokens)
    {
        try
        {
            await _email.SendAsync(EmailTemplateKey.Invitation, recipient, tokens);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invitation email to {Recipient} failed.", recipient);
            return false;
        }
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        var invite = await _db.Invitations.FindAsync(id);
        if (invite is not null && invite.AcceptedAt is null)
        {
            _db.Invitations.Remove(invite);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
