using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// Reference (lookup) table of U.S. states + DC used to populate the destination
// State dropdown when Country is the United States. Seeded once at startup.
public class UsState
{
    public int Id { get; set; }

    // Two-letter USPS abbreviation (e.g. "CA").
    [Required, StringLength(2)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;
}
