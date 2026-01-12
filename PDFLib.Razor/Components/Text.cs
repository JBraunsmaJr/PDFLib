using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using PDFLib.Enums;

namespace PDFLib.Components;

public class Text : ComponentBase
{
    [Parameter] public int FontSize { get; set; } = 12;
    [Parameter] public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;
    [Parameter] public string? Color { get; set; }
    [Parameter] public string? BackgroundColor { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "fontsize", FontSize);
        builder.AddAttribute(2, "align", Align);
        if (Color != null) builder.AddAttribute(3, "color", Color);
        if (BackgroundColor != null) builder.AddAttribute(4, "backgroundcolor", BackgroundColor);
        builder.AddContent(5, ChildContent);
        builder.CloseElement();
    }
}