using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// A cost center travelers charge trips to. Managed by Finance/Admin. Kept as
// reference data (mirrors Department): trips link by FK so a rename never
// rewrites history, and IsActive retires a code without deleting past links.
public class CostCenter
{
    public int Id { get; set; }

    // Short token an org keys on (e.g. "ENG-100"). Globally unique.
    [Required, StringLength(40), Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    // Human-readable label (e.g. "Engineering — Platform").
    [StringLength(120), Display(Name = "Name")]
    public string? Name { get; set; }

    // Retired codes stay linked to old trips but drop out of pickers.
    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
