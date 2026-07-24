using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// Reference (lookup) table of countries used to populate the destination Country
// dropdown. Seeded once at startup from ReferenceData; rows are stable.
public class Country
{
    public int Id { get; set; }

    // ISO 3166-1 alpha-2 code (e.g. "US").
    [Required, StringLength(2)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;
}
