using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public class Document : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Header { get; set; }
    [Parameter] public RenderFragment? Footer { get; set; }

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
