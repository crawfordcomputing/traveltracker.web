namespace TravelTracker.Web.Data.Entities;

// Scopes a travel arranger (Arranger role) to the specific travelers they may act
// for. Without a row here an Arranger reaches only their own trips; each row grants
// them the same access to one traveler's trips that the traveler has themselves.
// Managers/Admins keep global reach and don't use this table.
public class ArrangerAssignment
{
    public int Id { get; set; }

    public string ArrangerId { get; set; } = "";   // AppUser holding the Arranger role
    public AppUser? Arranger { get; set; }

    public string TravelerId { get; set; } = "";    // AppUser they may act for
    public AppUser? Traveler { get; set; }
}
