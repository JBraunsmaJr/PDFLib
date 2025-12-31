using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public enum ListType
{
    Bullet,
    Numbered
}

public class List : ComponentBase
{
    [Parameter] public ListType Type { get; set; } = ListType.Bullet;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "list");
        builder.AddAttribute(1, "type", Type.ToString());
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}

public class ListItem : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "list-item");
        builder.AddContent(1, ChildContent);
        builder.CloseElement();
    }
}
