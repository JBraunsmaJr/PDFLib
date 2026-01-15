using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that represents a vertical stack for layout purposes.
/// </summary>
public class Stack : ComponentBase
{
    /// <summary>
    /// Gets or sets the background color of the stack.
    /// </summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the padding for the stack.
    /// </summary>
    [Parameter] public int Padding { get; set; }

    /// <summary>
    /// Gets or sets the child content of the stack.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "stack");
        if (BackgroundColor != null) builder.AddAttribute(1, "backgroundcolor", BackgroundColor);
        if (Padding != 0) builder.AddAttribute(2, "padding", Padding);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }
}