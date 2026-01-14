using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that represents a digital signature field in the PDF.
/// </summary>
public class Signature : ComponentBase
{
    /// <summary>
    /// Gets or sets the X coordinate of the signature field.
    /// </summary>
    [Parameter] public int X { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate of the signature field.
    /// </summary>
    [Parameter] public int Y { get; set; }

    /// <summary>
    /// Gets or sets the width of the signature field.
    /// </summary>
    [Parameter] public int Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the signature field.
    /// </summary>
    [Parameter] public int Height { get; set; }

    /// <summary>
    /// Gets or sets the unique name of the signature field.
    /// </summary>
    [Parameter] public string? Name { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "signature");
        builder.AddAttribute(1, "x", X);
        builder.AddAttribute(2, "y", Y);
        builder.AddAttribute(3, "width", Width);
        builder.AddAttribute(4, "height", Height);
        if (!string.IsNullOrEmpty(Name)) builder.AddAttribute(5, "name", Name);
        builder.CloseElement();
    }
}