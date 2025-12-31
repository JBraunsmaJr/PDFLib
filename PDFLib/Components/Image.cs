using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public class Image : ComponentBase
{
    [Parameter] public string? Source { get; set; }
    [Parameter] public int Width { get; set; } = 100;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "image");
        builder.AddAttribute(1, "source", Source);
        builder.AddAttribute(2, "width", Width);
        builder.CloseElement();
    }
}
