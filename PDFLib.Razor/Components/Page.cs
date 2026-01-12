using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public class Page : ComponentBase
{
    [Parameter] public int Padding { get; set; } = 20;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Header { get; set; }
    [Parameter] public RenderFragment? Footer { get; set; }
    [Parameter] public RenderFragment? FirstPageHeader { get; set; }
    [Parameter] public RenderFragment? FirstPageFooter { get; set; }

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