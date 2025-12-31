using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public class Text : ComponentBase
{
    [Parameter] public int FontSize { get; set; } = 12;
    [Parameter] public string Align { get; set; } = "Left";
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "fontsize", FontSize);
        builder.AddAttribute(2, "align", Align);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }
}
