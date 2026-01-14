using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that represents the entire PDF document.
/// </summary>
public class Document : ComponentBase
{
    /// <summary>
    /// Gets or sets the child content (pages) of the document.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the global header for the document.
    /// </summary>
    [Parameter] public RenderFragment? Header { get; set; }

    /// <summary>
    /// Gets or sets the global footer for the document.
    /// </summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "document");

        if (Header != null)
        {
            builder.OpenElement(1, "header");
            builder.AddContent(2, Header);
            builder.CloseElement();
        }

        builder.AddContent(3, ChildContent);

        if (Footer != null)
        {
            builder.OpenElement(4, "footer");
            builder.AddContent(5, Footer);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}