using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace TravelTracker.Web.TagHelpers;

// Prepends a short "* Required field" legend to any <form> that rendered at least
// one required label (see RequiredLabelTagHelper). No per-view edits; forms with no
// required fields get nothing. Opt out on tight inline forms with
// tt-required-legend="false".
//
// The legend is aria-hidden: it only explains the visual asterisk, and screen
// readers already announce "required" per control via aria-required.
[HtmlTargetElement("form")]
public sealed class RequiredLegendFormTagHelper : TagHelper
{
    private const string Legend =
        "<p class=\"tt-required-legend\" aria-hidden=\"true\">"
        + "<span class=\"tt-required\">*</span> Required field</p>";

    [HtmlAttributeName("tt-required-legend")]
    public bool ShowLegend { get; set; } = true;

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    private int _labelsBefore;

    // Snapshot in Init, not ProcessAsync: Init runs for every tag helper on the element
    // before any Process call, and the built-in RenderAtEndOfFormTagHelper (Order -1000)
    // renders the child content first, so a snapshot taken in ProcessAsync is too late.
    public override void Init(TagHelperContext context)
        => _labelsBefore = RequiredFields.LabelCount(ViewContext.HttpContext);

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!ShowLegend) return;

        await output.GetChildContentAsync();
        if (RequiredFields.LabelCount(ViewContext.HttpContext) > _labelsBefore)
        {
            output.PreContent.AppendHtml(Legend);
        }
    }
}
