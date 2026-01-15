using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PDFLib.Components;

/// <summary>
/// Specifies the type of list.
/// </summary>
public enum ListType
{
    /// <summary>
    /// A bulleted list.
    /// </summary>
    Bullet,

    /// <summary>
    /// A numbered list.
    /// </summary>
    Numbered
}

/// <summary>
/// A Blazor component that renders a list (bulleted or numbered).
/// </summary>
public class List : ComponentBase
{
    /// <summary>
    /// Gets or sets the type of list. Defaults to <see cref="ListType.Bullet"/>.
    /// </summary>
    [Parameter] public ListType Type { get; set; } = ListType.Bullet;

    /// <summary>
    /// Gets or sets the list items.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "list");
        builder.AddAttribute(1, "type", Type.ToString());
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}

/// <summary>
/// A Blazor component that represents an item in a <see cref="List"/>.
/// </summary>
public class ListItem : ComponentBase
{
    /// <summary>
    /// Gets or sets the content of the list item.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "list-item");
        builder.AddContent(1, ChildContent);
        builder.CloseElement();
    }
}