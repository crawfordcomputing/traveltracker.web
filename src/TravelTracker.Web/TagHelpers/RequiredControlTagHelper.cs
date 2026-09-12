using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace TravelTracker.Web.TagHelpers;

// Adds aria-required="true" to input/select/textarea bound to a [Required] property.
// Deliberately not the native `required` attribute: jQuery unobtrusive validation
// stays the single source of validation messages and styling.
[HtmlTargetElement("input", Attributes = ForAttributeName, TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("select", Attributes = ForAttributeName)]
[HtmlTargetElement("textarea", Attributes = ForAttributeName)]
public sealed class RequiredControlTagHelper : TagHelper
{
    private const string ForAttributeName = "asp-for";

    // Run after the built-in Input/Select/TextArea tag helpers so `type` is resolved.
    public override int Order => 1000;

    [HtmlAttributeName(ForAttributeName)]
    public ModelExpression For { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!RequiredFields.IsRequired(For)) return;
        if (output.Attributes.ContainsName("aria-required")) return;
        if (output.Attributes.TryGetAttribute("type", out var type)
            && string.Equals(type.Value?.ToString(), "hidden", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        output.Attributes.SetAttribute("aria-required", "true");
    }
}
