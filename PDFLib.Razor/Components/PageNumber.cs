using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using PDFLib.Enums;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that renders the current page number and total page count.
/// </summary>
public class PageNumber : ComponentBase
{
    /// <summary>
    /// Gets or sets the display format. Use {page} for current page and {count} for total pages.
    /// Defaults to "Page {page} of {count}".
    /// </summary>
    [Parameter] public string Format { get; set; } = "Page {page} of {count}";

    /// <summary>
    /// Gets or sets the horizontal alignment of the page number. Defaults to <see cref="HorizontalAlignment.Left"/>.
    /// </summary>
    [Parameter] public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "page-number");
        builder.AddAttribute(1, "format", Format);
        builder.AddAttribute(2, "align", Align);
        builder.CloseElement();
    }
}