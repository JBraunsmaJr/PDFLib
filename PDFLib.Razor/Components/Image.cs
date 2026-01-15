using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that renders an image.
/// </summary>
public class Image : ComponentBase
{
    /// <summary>
    /// Gets or sets the source of the image (file path or URL).
    /// </summary>
    [Parameter] public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the width of the image. Defaults to 100.
    /// </summary>
    [Parameter] public int Width { get; set; } = 100;

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "image");
        builder.AddAttribute(1, "source", Source);
        builder.AddAttribute(2, "width", Width);
        builder.CloseElement();
    }
}