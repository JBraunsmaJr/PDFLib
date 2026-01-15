using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using PDFLib.Enums;

namespace PDFLib.Components;

/// <summary>
/// A Blazor component that renders text content.
/// </summary>
public class Text : ComponentBase
{
    /// <summary>
    /// Gets or sets the font size. Defaults to 12.
    /// </summary>
    [Parameter] public int FontSize { get; set; } = 12;

    /// <summary>
    /// Gets or sets the horizontal alignment. Defaults to <see cref="HorizontalAlignment.Left"/>.
    /// </summary>
    [Parameter] public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;

    /// <summary>
    /// Gets or sets the text color (hex or named).
    /// </summary>
    [Parameter] public string? Color { get; set; }

    /// <summary>
    /// Gets or sets the background color (hex or named).
    /// </summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the child content to be rendered.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
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