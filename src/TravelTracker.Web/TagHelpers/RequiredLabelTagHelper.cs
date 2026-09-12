using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace TravelTracker.Web.TagHelpers;

// Appends a visual "*" to <label asp-for> when the bound property has [Required].
// The marker is aria-hidden: screen readers get "required" from aria-required on
// the control instead (RequiredControlTagHelper), so it is not read twice.
[HtmlTargetElement("label", Attributes = ForAttributeName)]
public sealed class RequiredLabelTagHelper : TagHelper
{
    private const string ForAttributeName = "asp-for";
    private const string Marker = "<span class=\"tt-required\" aria-hidden=\"true\">*</span>";

    // Run after the built-in LabelTagHelper has filled in the display name.
    public override int Order => 1000;

    [HtmlAttributeName(ForAttributeName)]
    public ModelExpression For { get; set; } = default!;

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!RequiredFields.IsRequired(For)) return;

        output.PostContent.AppendHtml(Marker);
        RequiredFields.CountLabel(ViewContext.HttpContext);
    }
}
