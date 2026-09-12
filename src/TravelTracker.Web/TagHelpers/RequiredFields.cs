using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace TravelTracker.Web.TagHelpers;

// Shared rules for the required-field tag helpers (backlog #14).
//
// Keys off an EXPLICIT [Required] on the bound property, not ModelMetadata.IsRequired.
// With <Nullable>enable</Nullable>, IsRequired is also true for every non-nullable
// value type and string, which would mark optional enums (Category, TripType) and
// bools. Explicit [Required] is the "user must fill this" signal.
public static class RequiredFields
{
    private static readonly object LabelCountKey = new();

    public static bool IsRequired(ModelExpression? expression)
        => expression?.Metadata is DefaultModelMetadata metadata
           && metadata.Attributes.PropertyAttributes?.OfType<RequiredAttribute>().Any() == true;

    // Per-request count of required labels rendered so far. The form tag helper diffs
    // it around its child content, which (unlike TagHelperContext.Items) also sees
    // labels rendered inside partials such as _TravelerProfileFields.
    public static int LabelCount(HttpContext http)
        => http.Items.TryGetValue(LabelCountKey, out var value) && value is int count ? count : 0;

    public static void CountLabel(HttpContext http)
        => http.Items[LabelCountKey] = LabelCount(http) + 1;
}
