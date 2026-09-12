using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly EmailConfirmationService _confirmations;

    public RegisterModel(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        IConfiguration config,
        AppDbContext db,
        EmailConfirmationService confirmations)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _db = db;
        _confirmations = confirmations;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; set; }

    // View state.
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    public bool IsInvite { get; private set; }          // redeeming a valid invite
    public bool RegistrationAvailable { get; private set; } = true; // self-serve or valid invite

    public class InputModel
    {
        [Required, Display(Name = "Display name")] public string DisplayName { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
        [DataType(DataType.Password), Display(Name = "Confirm password"),
         Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!string.IsNullOrWhiteSpace(Token))
        {
            var invite = await FindRedeemableInviteAsync(Token);
            if (invite is null) { RegistrationAvailable = false; return Page(); }

            IsInvite = true;
            Input.Email = invite.Email;          // fixed by the invite
            return Page();
        }

        // No token: only Open/Domain expose the self-serve form.
        RegistrationAvailable = RegistrationPolicy.SelfServeAllowed(_config);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        // ---- Invite path -------------------------------------------------
        if (!string.IsNullOrWhiteSpace(Token))
        {
            var invite = await FindRedeemableInviteAsync(Token);
            if (invite is null) { RegistrationAvailable = false; return Page(); }

            IsInvite = true;
            Input.Email = invite.Email;          // ignore any tampered email field

            if (!TryValidateInviteInput()) return Page();

            // An admin may have created the account directly since the invite was
            // sent. Say so plainly instead of surfacing Identity's duplicate-email
            // error on a form the invitee can't fix.
            if (await _userManager.FindByEmailAsync(invite.Email) is not null)
            {
                ModelState.AddModelError(string.Empty,
                    "An account already exists for this email. Sign in instead, or use Forgot password if you don't have one yet.");
                return Page();
            }

            var invitedUser = new AppUser
            {
                UserName = invite.Email,
                Email = invite.Email,
                DisplayName = Input.DisplayName.Trim(),
                DepartmentId = invite.DepartmentId,
                // Receiving the invite at this address already proves ownership, so
                // invited users skip email confirmation and sign straight in below.
                EmailConfirmed = true
            };

            var created = await _userManager.CreateAsync(invitedUser, Input.Password);
            if (!created.Succeeded)
            {
                AddErrors(created);
                return Page();
            }

            var role = Roles.All.Contains(invite.Role) ? invite.Role : Roles.Employee;
            await _userManager.AddToRoleAsync(invitedUser, role);

            invite.AcceptedAt = DateTimeOffset.UtcNow;   // spend the invite
            await _db.SaveChangesAsync();

            await _signInManager.SignInAsync(invitedUser, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        // ---- Self-serve path (Open / Domain) -----------------------------
        if (!RegistrationPolicy.SelfServeAllowed(_config))
            return Forbid();

        RegistrationAvailable = true;
        if (!ModelState.IsValid) return Page();

        if (!RegistrationPolicy.EmailAllowed(_config, Input.Email))
        {
            ModelState.AddModelError("Input.Email",
                "Registration is restricted to approved company email addresses.");
            return Page();
        }

        var user = new AppUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            DisplayName = Input.DisplayName.Trim()
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            // Self-registered users start as Employees; admins can promote.
            await _userManager.AddToRoleAsync(user, Roles.Employee);

            // When email confirmation is required (prod), email the link and land on
            // a "check your email" page instead of signing in — the account can't
            // sign in until confirmed. When off (dev), keep the frictionless flow.
            if (_userManager.Options.SignIn.RequireConfirmedAccount)
            {
                await _confirmations.SendLinkAsync(user, (userId, token) =>
                    Url.Page("/Account/ConfirmEmail", pageHandler: null,
                        values: new { userId, token }, protocol: Request.Scheme)!);
                return RedirectToPage("/Account/RegisterConfirmation", new { email = user.Email });
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        AddErrors(result);
        return Page();
    }

    // Invite posts don't require the bound Email to validate (it's fixed by the
    // invite), so validate only the fields the registrant actually supplies.
    private bool TryValidateInviteInput()
    {
        ModelState.Remove("Input.Email");
        if (string.IsNullOrWhiteSpace(Input.DisplayName))
            ModelState.AddModelError("Input.DisplayName", "Display name is required.");
        if (string.IsNullOrEmpty(Input.Password) || Input.Password.Length < 8)
            ModelState.AddModelError("Input.Password", "Password must be at least 8 characters.");
        if (Input.Password != Input.ConfirmPassword)
            ModelState.AddModelError("Input.ConfirmPassword", "Passwords do not match.");
        return ModelState.IsValid;
    }

    private async Task<Invitation?> FindRedeemableInviteAsync(string token)
    {
        var hash = InviteTokens.Hash(token);
        var invite = await _db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash);
        return invite is not null && invite.IsRedeemable(DateTimeOffset.UtcNow) ? invite : null;
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
