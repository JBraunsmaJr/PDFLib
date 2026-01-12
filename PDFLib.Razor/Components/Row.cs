using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public class Row : ComponentBase
{
    [Parameter] public string? BackgroundColor { get; set; }
    [Parameter] public int Padding { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "row");
        if (BackgroundColor != null) builder.AddAttribute(1, "backgroundcolor", BackgroundColor);
        if (Padding != 0) builder.AddAttribute(2, "padding", Padding);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }
}