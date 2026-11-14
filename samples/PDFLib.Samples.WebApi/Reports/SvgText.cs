using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Samples.WebApi.Reports;

/// <summary>
/// Emits an SVG <c>&lt;text&gt;</c> element.
/// </summary>
/// <remarks>
/// Razor reserves the <c>&lt;text&gt;</c> tag for its own use (it marks a block of literal markup inside
/// code), so it cannot be written directly in a .razor file once it needs attributes. This component
/// writes the element through the render tree instead; every attribute given to it is passed through.
/// </remarks>
public sealed class SvgText : ComponentBase
{
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? Attributes { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddMultipleAttributes(1, Attributes);
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}
