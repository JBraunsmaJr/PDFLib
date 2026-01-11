using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public class Signature : ComponentBase
{
    [Parameter] public int X { get; set; }
    [Parameter] public int Y { get; set; }
    [Parameter] public int Width { get; set; }
    [Parameter] public int Height { get; set; }
    [Parameter] public string? Name { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "signature");
        builder.AddAttribute(1, "x", X);
        builder.AddAttribute(2, "y", Y);
        builder.AddAttribute(3, "width", Width);
        builder.AddAttribute(4, "height", Height);
        if (!string.IsNullOrEmpty(Name))
        {
            builder.AddAttribute(5, "name", Name);
        }
        builder.CloseElement();
    }
}
