using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Pure conversion between the no-JS "one stop per line" textarea and ordered
// MileageWaypoint rows. Keeping it here (not in the page) makes the parse rules —
// trim, drop blanks, cap length, renumber sequentially — unit-testable and shared
// by Create and Edit.
public static class WaypointText
{
    public const int MaxLabelLength = 160;

    // Split textarea input into trimmed, non-empty labels, each capped at
    // MaxLabelLength, in the order entered.
    public static List<string> ParseLabels(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        return text
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Length > MaxLabelLength ? l[..MaxLabelLength] : l)
            .ToList();
    }

    // Build sequentially-numbered waypoint rows from textarea input.
    public static List<MileageWaypoint> Parse(string? text) =>
        ParseLabels(text)
            .Select((label, i) => new MileageWaypoint { Sequence = i + 1, Label = label })
            .ToList();

    // Render ordered waypoints back to one-per-line text for the edit form.
    public static string Format(IEnumerable<MileageWaypoint> waypoints) =>
        string.Join("\n", waypoints.OrderBy(w => w.Sequence).Select(w => w.Label));
}
