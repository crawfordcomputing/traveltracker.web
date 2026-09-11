using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services.Email;

namespace TravelTracker.Web.Pages.Admin.EmailTemplates;

// Edit one (Key, Audience) variant (ADR-0005). Under /Admin (RequireAdmin).
// Save validates + sanitizes and upserts the override row; Reset deletes it (back
// to the built-in default); Send test mails the current draft to the signed-in
// admin; Preview returns the rendered draft as JSON for the sandboxed iframe.
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IEmailTemplateService _templates;
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _config;
    private readonly ILogger<EditModel> _logger;

    public EditModel(AppDbContext db, IEmailTemplateService templates, UserManager<AppUser> userManager,
        IConfiguration config, ILogger<EditModel> logger)
    {
        _db = db;
        _templates = templates;
        _userManager = userManager;
        _config = config;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)] public EmailTemplateKey Key { get; set; }
    [BindProperty(SupportsGet = true)] public EmailAudience Audience { get; set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    // "Which audience would this address get?" sanity check for Email:InternalDomains.
    [BindProperty(SupportsGet = true)] public string? CheckEmail { get; set; }
    public EmailAudience? CheckResult { get; private set; }

    public EmailTemplateTokens.Spec Spec { get; private set; } = default!;

    // The stored row for exactly this (Key, Audience), if any.
    public EmailTemplate? Existing { get; private set; }
    public string? UpdatedByName { get; private set; }

    // What a send to this audience uses today when this variant isn't customized:
    // "Any override" or "Built-in default". Null when this variant is customized.
    public string? InheritsFrom { get; private set; }

    public EmailTemplateContent? Preview { get; private set; }
    public IReadOnlyList<string> PreviewErrors { get; private set; } = Array.Empty<string>();

    public class InputModel
    {
        [Required, StringLength(EmailTemplateRenderer.MaxSubjectLength)]
        public string Subject { get; set; } = string.Empty;

        [Required, StringLength(EmailTemplateRenderer.MaxBodyLength), Display(Name = "HTML body")]
        public string HtmlBody { get; set; } = string.Empty;
    }

    private bool IsValidRoute() =>
        Enum.IsDefined(Key) && Enum.IsDefined(Audience);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsValidRoute()) return NotFound();
        await LoadAsync(prefill: true);
        if (!string.IsNullOrWhiteSpace(CheckEmail))
            CheckResult = RecipientClassifier.Classify(_config, CheckEmail);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!IsValidRoute()) return NotFound();

        var sanitized = EmailTemplateSanitizer.Sanitize(Input.HtmlBody ?? string.Empty);
        var changedBySanitizer = sanitized != (Input.HtmlBody ?? string.Empty);
        Input.HtmlBody = sanitized;
        Input.Subject = (Input.Subject ?? string.Empty).Trim();

        foreach (var e in EmailTemplateRenderer.Validate(Key, Input.Subject, Input.HtmlBody))
            ModelState.AddModelError(string.Empty, e);

        // Re-validate the sanitized value, not the posted one.
        ModelState.Remove("Input.HtmlBody");
        ModelState.Remove("Input.Subject");

        if (ModelState.ErrorCount > 0)
        {
            await LoadAsync(prefill: false);
            return Page();
        }

        var row = await _db.EmailTemplates
            .FirstOrDefaultAsync(t => t.Key == Key && t.Audience == Audience);
        if (row is null)
        {
            row = new EmailTemplate { Key = Key, Audience = Audience };
            _db.EmailTemplates.Add(row);
        }
        row.Subject = Input.Subject;
        row.HtmlBody = Input.HtmlBody;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedById = _userManager.GetUserId(User) ?? string.Empty;
        await _db.SaveChangesAsync();

        TempData["TemplateInfo"] = changedBySanitizer
            ? "Saved. Some unsafe HTML (scripts, event handlers, or javascript: links) was removed."
            : "Saved.";
        return RedirectToPage(new { key = Key, audience = Audience });
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (!IsValidRoute()) return NotFound();

        var row = await _db.EmailTemplates
            .FirstOrDefaultAsync(t => t.Key == Key && t.Audience == Audience);
        if (row is not null)
        {
            _db.EmailTemplates.Remove(row);
            await _db.SaveChangesAsync();
            TempData["TemplateInfo"] = "Reset to default.";
        }
        return RedirectToPage(new { key = Key, audience = Audience });
    }

    // Mails the current draft (saved or not) to the signed-in admin with sample data.
    public async Task<IActionResult> OnPostSendTestAsync()
    {
        if (!IsValidRoute()) return NotFound();

        var draft = Draft();
        var errors = EmailTemplateRenderer.Validate(Key, draft.Subject, draft.HtmlBody);
        var me = await _userManager.GetUserAsync(User);

        if (errors.Count > 0)
        {
            foreach (var e in errors) ModelState.AddModelError(string.Empty, e);
        }
        else if (string.IsNullOrWhiteSpace(me?.Email))
        {
            ModelState.AddModelError(string.Empty, "Your account has no email address to send a test to.");
        }
        else
        {
            try
            {
                await _templates.SendTestAsync(Key, Audience, draft, me.Email);
                TempData["TemplateInfo"] = $"Test email sent to {me.Email}. Check the notification log for the outcome.";
                // Keep the unsaved draft on screen rather than reloading the saved version.
                await LoadAsync(prefill: false);
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Test email for {Key}/{Audience} failed.", Key, Audience);
                ModelState.AddModelError(string.Empty, $"Test email failed: {ex.Message}");
            }
        }

        await LoadAsync(prefill: false);
        return Page();
    }

    // Live preview for the editor (fetch from JS). Renders the draft with sample
    // data; validation problems come back alongside so the admin sees both.
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (!IsValidRoute()) return NotFound();
        var me = await _userManager.GetUserAsync(User);
        var (preview, errors) = RenderPreview(Draft(), me?.Email);
        return new JsonResult(new { subject = preview?.Subject, html = preview?.HtmlBody, errors });
    }

    private EmailTemplateContent Draft() =>
        new((Input.Subject ?? string.Empty).Trim(), Input.HtmlBody ?? string.Empty);

    private (EmailTemplateContent? Preview, IReadOnlyList<string> Errors) RenderPreview(
        EmailTemplateContent draft, string? recipient)
    {
        var errors = EmailTemplateRenderer.Validate(Key, draft.Subject, draft.HtmlBody);
        try
        {
            // Preview what will actually be stored: the sanitized body.
            var clean = draft with { HtmlBody = EmailTemplateSanitizer.Sanitize(draft.HtmlBody) };
            return (_templates.RenderSample(Key, clean, recipient ?? "sam@example.com"), errors);
        }
        catch (EmailTemplateException)
        {
            return (null, errors);
        }
    }

    private async Task LoadAsync(bool prefill)
    {
        Spec = EmailTemplateTokens.For(Key);

        Existing = await _db.EmailTemplates.AsNoTracking()
            .Include(t => t.UpdatedBy)
            .FirstOrDefaultAsync(t => t.Key == Key && t.Audience == Audience);
        UpdatedByName = Existing?.UpdatedBy?.DisplayName;

        if (Existing is null)
        {
            // What sends use today for this audience, so editing starts from it.
            var inherited = await _templates.ResolveAsync(Key, Audience);
            InheritsFrom = inherited.IsDefault ? "the built-in default" : "the Any override";
            if (prefill)
            {
                Input.Subject = inherited.Content.Subject;
                Input.HtmlBody = inherited.Content.HtmlBody;
            }
        }
        else if (prefill)
        {
            Input.Subject = Existing.Subject;
            Input.HtmlBody = Existing.HtmlBody;
        }

        var me = await _userManager.GetUserAsync(User);
        (Preview, PreviewErrors) = RenderPreview(Draft(), me?.Email);
    }
}
