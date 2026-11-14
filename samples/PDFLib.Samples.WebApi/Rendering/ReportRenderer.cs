using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace PDFLib.Samples.WebApi.Rendering;

/// <summary>
/// Turns a Razor component into an HTML string, ready to hand to the PDF service.
/// </summary>
/// <remarks>
/// <see cref="HtmlRenderer"/> is the same server-side renderer Blazor uses for static rendering. It needs no
/// HTTP request, so reports can be produced from background jobs as well as from endpoints. A renderer is
/// cheap to create and not thread-safe, so each call gets its own.
/// </remarks>
public sealed class ReportRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
{
    /// <summary>Renders <typeparamref name="TReport"/> with its <c>Model</c> parameter set to <paramref name="model"/>.</summary>
    public async Task<string> RenderAsync<TReport>(object model) where TReport : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Model"] = model });
            var output = await renderer.RenderComponentAsync<TReport>(parameters);
            return output.ToHtmlString();
        });
    }
}
