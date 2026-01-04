namespace PDFApi.Services;

public interface IBrowsingService
{
    Task<Stream> RenderHtmlToPdfStreamAsync(string htmlContent);
}