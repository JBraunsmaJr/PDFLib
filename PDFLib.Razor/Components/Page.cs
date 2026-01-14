using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that represents a page in the PDF document.
/// </summary>
public class Page : ComponentBase
{
    /// <summary>
    /// Gets or sets the page padding. Defaults to 20.
    /// </summary>
    [Parameter] public int Padding { get; set; } = 20;

    /// <summary>
    /// Gets or sets the main content of the page.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the header content for all pages.
    /// </summary>
    [Parameter] public RenderFragment? Header { get; set; }

    /// <summary>
    /// Gets or sets the footer content for all pages.
    /// </summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    /// <summary>
    /// Gets or sets the header content specific to the first page.
    /// </summary>
    [Parameter] public RenderFragment? FirstPageHeader { get; set; }

    /// <summary>
    /// Gets or sets the footer content specific to the first page.
    /// </summary>
    [Parameter] public RenderFragment? FirstPageFooter { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "page");
        builder.AddAttribute(1, "padding", Padding);

        if (FirstPageHeader != null)
        {
            builder.OpenElement(2, "first-page-header");
            builder.AddContent(3, FirstPageHeader);
            builder.CloseElement();
        }

        if (Header != null)
        {
            builder.OpenElement(4, "header");
            builder.AddContent(5, Header);
            builder.CloseElement();
        }

        builder.AddContent(6, ChildContent);

        if (Footer != null)
        {
            builder.OpenElement(7, "footer");
            builder.AddContent(8, Footer);
            builder.CloseElement();
        }

        if (FirstPageFooter != null)
        {
            builder.OpenElement(9, "first-page-footer");
            builder.AddContent(10, FirstPageFooter);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}