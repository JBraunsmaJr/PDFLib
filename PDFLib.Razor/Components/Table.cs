using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that renders a table in the PDF.
/// </summary>
public class Table : ComponentBase
{
    /// <summary>
    /// Gets or sets the child content (rows).
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the comma-separated column widths.
    /// </summary>
    [Parameter] public string? ColumnWidths { get; set; } // Comma separated values

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "table");
        if (!string.IsNullOrEmpty(ColumnWidths)) builder.AddAttribute(1, "columnwidths", ColumnWidths);
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}

/// <summary>
/// A Blazor component that represents a row in a <see cref="Table"/>.
/// </summary>
public class TableRow : ComponentBase
{
    /// <summary>
    /// Gets or sets the child content (cells).
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "tr");
        builder.AddContent(1, ChildContent);
        builder.CloseElement();
    }
}

/// <summary>
/// A Blazor component that represents a cell in a <see cref="TableRow"/>.
/// </summary>
public class TableCell : ComponentBase
{
    /// <summary>
    /// Gets or sets the background color of the cell.
    /// </summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the text color within the cell.
    /// </summary>
    [Parameter] public string? Color { get; set; }

    /// <summary>
    /// Gets or sets the content of the cell.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "td");
        if (BackgroundColor != null) builder.AddAttribute(1, "backgroundcolor", BackgroundColor);
        if (Color != null) builder.AddAttribute(2, "color", Color);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }
}