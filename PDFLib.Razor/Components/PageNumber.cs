using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using PDFLib.Enums;

namespace PDFLib.Components;

public class PageNumber : ComponentBase
{
    [Parameter] public string Format { get; set; } = "Page {page} of {count}";
    [Parameter] public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "page-number");
        builder.AddAttribute(1, "format", Format);
        builder.AddAttribute(2, "align", Align);
        builder.CloseElement();
    }
}