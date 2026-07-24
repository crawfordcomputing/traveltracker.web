using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TravelTracker.Web.Pages.Account;

[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    // Landing after a self-serve registration that requires email confirmation.
    // Purely informational — the token lives in the emailed link, never here.
    public string? Email { get; private set; }

    public void OnGet(string? email) => Email = email;
}
