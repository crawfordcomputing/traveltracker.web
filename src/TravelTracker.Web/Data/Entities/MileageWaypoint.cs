using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// One ordered stop on a mileage entry's route. Lets an entry describe a real
// multi-stop trip (A→B→C), not just a single point-to-point hop. Purely
// descriptive labels — the billable distance is the entry's own Distance figure,
// so no geocoding/maps dependency is pulled in.
public class MileageWaypoint
{
    public int Id { get; set; }

    public int MileageEntryId { get; set; }
    public MileageEntry? MileageEntry { get; set; }

    // 1-based position in the route.
    public int Sequence { get; set; }

    [Required, StringLength(160)]
    public string Label { get; set; } = string.Empty;
}
