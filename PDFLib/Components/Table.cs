using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

public class Table : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? ColumnWidths { get; set; } // Comma separated values

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "table");
        if (!string.IsNullOrEmpty(ColumnWidths))
        {
            builder.AddAttribute(1, "columnwidths", ColumnWidths);
        }
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}

public class TableRow : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "tr");
        builder.AddContent(1, ChildContent);
        builder.CloseElement();
    }
}

public class TableCell : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "td");
        builder.AddContent(1, ChildContent);
        builder.CloseElement();
    }
}
