using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// A project/charge code travelers assign trips to. Managed by Finance, Managers,
// and Admins. Same reference-data model as CostCenter: FK-linked from Trip so a
// rename never rewrites history, IsActive retires a code without deleting links.
public class ProjectCode
{
    public int Id { get; set; }

    // Short token an org keys on (e.g. "PRJ-2026-04"). Globally unique.
    [Required, StringLength(40), Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    // Human-readable label (e.g. "Q2 Field Rollout").
    [StringLength(120), Display(Name = "Name")]
    public string? Name { get; set; }

    // Retired codes stay linked to old trips but drop out of pickers.
    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
